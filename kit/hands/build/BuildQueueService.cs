namespace Services.Build;

/// <summary>
/// 编译队列服务 — 串行处理编译请求，集成跨进程编译锁、结果缓冲（源指纹校验）、
/// 防睡眠、取消与状态查询能力。通过 Channel 实现单消费者串行执行。
/// </summary>
[Register(typeof(IBuildQueueService), ServiceLifetime.Singleton)]
public sealed partial class BuildQueueService : BuildQueueBase {
    private readonly ISystemActuatorRegistry _actuatorRegistry;
    private readonly IFileSystem _fs;
    private readonly IPreventSleepService? _preventSleepService;
    private readonly ILogger<BuildQueueService>? _logger;

    private readonly Channel<BuildQueueEntry> _queue = Channel.CreateBounded<BuildQueueEntry>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _processingTask;

    private readonly CrossProcessBuildLock _crossProcessLock;
    private readonly BuildResultBuffer _resultBuffer;

    private BuildQueueEntry? _currentBuild;
    private CancellationTokenSource? _currentBuildCts;

    /// <summary>
    /// 构造编译队列服务，启动后台串行处理任务。
    /// </summary>
    /// <param name="actuatorRegistry">系统执行器注册表（获取 Bash 执行编译）。</param>
    /// <param name="fs">文件系统抽象。</param>
    /// <param name="preventSleepService">防睡眠服务（可选）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="crossProcessLockPath">跨进程锁文件路径（可选，默认自动定位 .git 目录）。</param>
    public BuildQueueService(
        ISystemActuatorRegistry actuatorRegistry,
        IFileSystem fs,
        IPreventSleepService? preventSleepService = null,
        ILogger<BuildQueueService>? logger = null,
        string? crossProcessLockPath = null) {
        _actuatorRegistry = actuatorRegistry;
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _preventSleepService = preventSleepService;
        _logger = logger;

        var fingerprintCache = new SourceFingerprintCache(logger);
        _resultBuffer = new BuildResultBuffer(fingerprintCache, logger);
        _crossProcessLock = new CrossProcessBuildLock(fs, logger, crossProcessLockPath);

        _processingTask = ProcessQueueAsync(_shutdownCts.Token);
    }

    /// <summary>
    /// 提交编译请求到队列。若结果缓冲命中（源指纹未变）则直接返回已完成的构建 ID。
    /// </summary>
    /// <param name="request">编译请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>构建 ID。</returns>
    public override Task<string> SubmitAsync(BuildRequest request, CancellationToken ct) {
        var bufferKey = BuildResultBuffer.BuildBufferKey(request.Command, request.WorkingDirectory);

        if (_resultBuffer.TryGet(bufferKey, out var bufferedResult)) {
            _logger?.LogInformation("Build result buffer hit: {BufferKey}", bufferKey);
            var buildId = CreateCompletedEntry(request, bufferedResult);
            return Task.FromResult(buildId);
        }

        var (newEntry, _) = CreateQueuedEntry(request);
        _queue.Writer.TryWrite(newEntry);

        _logger?.LogInformation("Build submitted: {BuildId}, command: {Command}", newEntry.BuildId, request.Command);

        return Task.FromResult(newEntry.BuildId);
    }

    /// <inheritdoc />
    public override Task<bool> CancelAsync(string buildId, CancellationToken ct) {
        if (!_store.TryGetEntry(buildId, out var entry))
            return Task.FromResult(false);

        switch (entry.Status) {
            case BuildQueueEntryStatus.Queued:
            entry.Status = BuildQueueEntryStatus.Cancelled;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            CompleteWithCancellation(buildId, entry);
            _logger?.LogInformation("Build cancelled (was queued): {BuildId}", buildId);
            return Task.FromResult(true);

            case BuildQueueEntryStatus.Building:
            entry.Status = BuildQueueEntryStatus.Cancelling;
            _currentBuildCts?.Cancel();
            _logger?.LogInformation("Build cancelling (was building): {BuildId}", buildId);
            return Task.FromResult(true);

            default:
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public override BuildQueueStatus GetStatus() {
        var pendingCount = _store.GetAllEntries().Count(e => e.Status == BuildQueueEntryStatus.Queued);
        var isBuilding = _currentBuild is not null && _currentBuild.Status == BuildQueueEntryStatus.Building;

        return new BuildQueueStatus {
            PendingCount = pendingCount,
            IsBuilding = isBuilding,
            CurrentBuildId = _currentBuild?.BuildId,
            CurrentBuildAgentId = _currentBuild?.Request.AgentId,
            RecentBuilds = _store.GetAllEntries()
                .OrderByDescending(e => e.Request.SubmittedAt)
                .Take(10)
                .ToList()
        };
    }

    /// <inheritdoc />
    public override Task ClearCacheAsync(CancellationToken ct) {
        _resultBuffer.Clear();
        return Task.CompletedTask;
    }

    private string CreateCompletedEntry(BuildRequest request, BuildQueueResult result) {
        var buildId = NextBuildId();
        var entry = new BuildQueueEntry {
            BuildId = buildId,
            Request = request,
            Status = BuildQueueEntryStatus.Completed,
            Result = result with { BuildId = buildId },
            CompletedAt = DateTimeOffset.UtcNow,
        };
        var tcs = new TaskCompletionSource<BuildQueueResult>();
        tcs.TrySetResult(result with { BuildId = buildId });
        _store.Add(buildId, entry, tcs);
        return buildId;
    }

    private async Task ProcessQueueAsync(CancellationToken ct) {
        await foreach (var entry in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            if (entry.Status == BuildQueueEntryStatus.Cancelled) continue;

            _currentBuild = entry;
            entry.Status = BuildQueueEntryStatus.Building;
            entry.StartedAt = DateTimeOffset.UtcNow;

            _logger?.LogInformation("Build {BuildId} started (checkpoint): queuePos={QueuePos}, pending={Pending}",
                entry.BuildId, entry.QueuePosition, _store.GetAllEntries().Count(e => e.Status == BuildQueueEntryStatus.Queued));

            var waitStart = DateTimeOffset.UtcNow;

            try {
                var result = await ExecuteBuildAsync(entry, ct).ConfigureAwait(false);
                entry.Result = result;
                entry.CompletedAt = DateTimeOffset.UtcNow;

                entry.Status = result.Cancelled
                    ? BuildQueueEntryStatus.Cancelled
                    : result.ExitCode == 0
                        ? BuildQueueEntryStatus.Completed
                        : BuildQueueEntryStatus.Failed;

                var bufferKey = BuildResultBuffer.BuildBufferKey(entry.Request.Command, entry.Request.WorkingDirectory);
                _resultBuffer.Add(bufferKey, result, entry.Request.WorkingDirectory);

                _store.TryGetTcs(entry.BuildId, out var tcs);
                tcs?.TrySetResult(result);

                _logger?.LogInformation("Build {BuildId} completed: {Status}, exit={ExitCode}",
                    entry.BuildId, entry.Status, result.ExitCode);
            } catch (OperationCanceledException) when (entry.Status == BuildQueueEntryStatus.Cancelling) {
                entry.Status = BuildQueueEntryStatus.Cancelled;
                entry.CompletedAt = DateTimeOffset.UtcNow;
                CompleteWithCancellation(entry.BuildId, entry);
                _logger?.LogInformation("Build {BuildId} cancelled", entry.BuildId);
            } catch (Exception ex) {
                entry.Status = BuildQueueEntryStatus.Failed;
                entry.CompletedAt = DateTimeOffset.UtcNow;

                var failResult = new BuildQueueResult {
                    BuildId = entry.BuildId,
                    ExitCode = -1,
                    Output = string.Empty,
                    ErrorOutput = ex.Message,
                    WaitDuration = DateTimeOffset.UtcNow - waitStart,
                    BuildDuration = TimeSpan.Zero,
                    QueuePosition = entry.QueuePosition
                };
                entry.Result = failResult;

                _store.TryGetTcs(entry.BuildId, out var tcs);
                tcs?.TrySetResult(failResult);

                _logger?.LogError(ex, "Build {BuildId} failed with exception", entry.BuildId);
            } finally {
                _currentBuild = null;
                _currentBuildCts?.Dispose();
                _currentBuildCts = null;

                _logger?.LogDebug("Build {BuildId} checkpoint: status={Status}, remaining={Remaining}",
                    entry.BuildId, entry.Status, _store.GetAllEntries().Count(e => e.Status == BuildQueueEntryStatus.Queued));
            }
        }
    }

    private async Task<BuildQueueResult> ExecuteBuildAsync(BuildQueueEntry entry, CancellationToken ct) {
        await using var scope = new BuildExecutionScope(this, ct);
        var buildCt = scope.Token;

        if (buildCt.IsCancellationRequested) {
            return BuildQueueBase.CreateCancelledResult(entry, "Build cancelled while waiting for build lock");
        }

        try {
            await scope.AcquireLockAsync(buildCt).ConfigureAwait(false);
            _logger?.LogInformation("Build lock acquired for {BuildId} via {LockPath}",
                entry.BuildId, _crossProcessLock.LockPath);
        } catch (OperationCanceledException) {
            return BuildQueueBase.CreateCancelledResult(entry, "Build cancelled while waiting for build lock");
        }

        return await BuildQueueBase.ExecuteBuildCoreAsync(
            entry, _actuatorRegistry, _preventSleepService, _logger, buildCt,
            preferResultExecutionTime: false).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步释放编译队列服务资源：取消后台处理、完成队列、释放等待句柄、
    /// 释放跨进程锁。幂等，多次调用安全。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public override async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _shutdownCts.Cancel();
        _queue.Writer.TryComplete();

        try {
            _ = _processingTask;
        } catch (OperationCanceledException) { }

        _store.CancelAll();

        _shutdownCts.Dispose();
        _currentBuildCts?.Dispose();
        await _crossProcessLock.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class BuildExecutionScope : IAsyncDisposable {
        private readonly BuildQueueService _owner;
        private readonly CancellationTokenSource _cts;
        private bool _lockAcquired;
        private int _disposed;

        /// <summary>构造构建执行作用域。</summary>
        /// <param name="owner">所属构建队列服务。</param>
        /// <param name="externalCt">外部取消令牌。</param>
        public BuildExecutionScope(BuildQueueService owner, CancellationToken externalCt) {
            _owner = owner;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            _owner._currentBuildCts = _cts;
        }

        /// <summary>获取关联的取消令牌。</summary>
        public CancellationToken Token => _cts.Token;

        /// <summary>异步获取跨进程构建锁。</summary>
        /// <param name="ct">取消令牌。</param>
        public async Task AcquireLockAsync(CancellationToken ct) {
            await _owner._crossProcessLock.AcquireAsync(ct).ConfigureAwait(false);
            _lockAcquired = true;
        }

        /// <summary>异步释放资源。</summary>
        public ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return default;

            if (_lockAcquired) {
                _owner._crossProcessLock.Release();
            }

            if (_owner._currentBuildCts == _cts) {
                _owner._currentBuildCts = null;
            }

            _cts.Dispose();
            return default;
        }
    }
}