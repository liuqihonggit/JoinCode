namespace Services.Build;

/// <summary>构建队列命令标记接口 — Actor 串行化命令处理。</summary>
public interface IBuildQueueCommand;

/// <summary>提交构建命令 — 入队或立即执行。</summary>
internal sealed record SubmitCmd(BuildQueueEntry Entry) : IBuildQueueCommand;

/// <summary>取消构建命令 — 取消正在执行或排队的构建，Tcs 回复是否成功。</summary>
internal sealed record CancelCmd(string BuildId, TaskCompletionSource<bool> Tcs) : IBuildQueueCommand;

/// <summary>构建完成命令 — fire-and-forget 构建完成后回投 Actor，串行化结果处理 + 启动下一个。</summary>
internal sealed record BuildCompletedCmd(BuildQueueEntry Entry, BuildQueueResult? Result, Exception? Error) : IBuildQueueCommand;

/// <summary>
/// 编译队列服务 — 串行处理编译请求，集成跨进程编译锁、结果缓冲（源指纹校验）、
/// 防睡眠、取消与状态查询能力。
/// <para>Actor 模型：内部组合 BuildQueueActor（ActorBase），命令串行化，构建执行 fire-and-forget + 回投。</para>
/// <para>竞态消除：CancelAsync 通过 Tell 投递，不再直接修改 entry.Status。GetStatus 用 Volatile.Read 快照。</para>
/// </summary>
[Register(typeof(IBuildQueueService), ServiceLifetime.Singleton)]
public sealed partial class BuildQueueService : BuildQueueBase {
    private readonly ISystemActuatorRegistry _actuatorRegistry;
    private readonly IFileSystem _fs;
    private readonly IPreventSleepService? _preventSleepService;
    private readonly ILogger<BuildQueueService>? _logger;

    private readonly CrossProcessBuildLock _crossProcessLock;
    private readonly BuildResultBuffer _resultBuffer;
    private readonly BuildQueueActor _actor;

    private BuildQueueEntry? _currentBuild;
    private CancellationTokenSource? _currentBuildCts;

    /// <summary>
    /// 构造编译队列服务，启动 Actor 消费循环。
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
        _actor = new BuildQueueActor(this, logger);
    }

    /// <summary>
    /// 提交编译请求到队列。若结果缓冲命中（源指纹未变）则直接返回已完成的构建 ID。
    /// </summary>
    public override Task<string> SubmitAsync(BuildRequest request, CancellationToken ct) {
        ThrowIfDisposed(nameof(BuildQueueService));

        var bufferKey = BuildResultBuffer.BuildBufferKey(request.Command, request.WorkingDirectory);

        if (_resultBuffer.TryGet(bufferKey, out var bufferedResult)) {
            _logger?.LogInformation("Build result buffer hit: {BufferKey}", bufferKey);
            var buildId = CreateCompletedEntry(request, bufferedResult);
            return Task.FromResult(buildId);
        }

        var (newEntry, _) = CreateQueuedEntry(request);
        _actor.Tell(new SubmitCmd(newEntry));

        _logger?.LogInformation("Build submitted: {BuildId}, command: {Command}", newEntry.BuildId, request.Command);

        return Task.FromResult(newEntry.BuildId);
    }

    /// <inheritdoc />
    public override Task<bool> CancelAsync(string buildId, CancellationToken ct) {
        ThrowIfDisposed(nameof(BuildQueueService));
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _actor.Tell(new CancelCmd(buildId, tcs));
        return tcs.Task;
    }

    /// <inheritdoc />
    public override BuildQueueStatus GetStatus() {
        var currentBuild = Volatile.Read(ref _currentBuild);
        var pendingCount = _store.GetAllEntries().Count(e => e.Status == BuildQueueEntryStatus.Queued);
        var isBuilding = currentBuild is not null && currentBuild.Status == BuildQueueEntryStatus.Building;

        return new BuildQueueStatus {
            PendingCount = pendingCount,
            IsBuilding = isBuilding,
            CurrentBuildId = currentBuild?.BuildId,
            CurrentBuildAgentId = currentBuild?.Request.AgentId,
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
    /// 异步释放编译队列服务资源：释放 Actor（等 Consumer 退出 + in-flight 完成）、
    /// 取消所有等待句柄、释放跨进程锁。幂等。
    /// </summary>
    public override async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        await _actor.DisposeAsync().ConfigureAwait(false);

        _store.CancelAll();
        _currentBuildCts?.Dispose();
        await _crossProcessLock.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class BuildExecutionScope : IAsyncDisposable {
        private readonly BuildQueueService _owner;
        private readonly CancellationTokenSource _cts;
        private bool _lockAcquired;
        private int _disposed;

        /// <summary>构造构建执行作用域。</summary>
        public BuildExecutionScope(BuildQueueService owner, CancellationToken externalCt) {
            _owner = owner;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            Volatile.Write(ref _owner._currentBuildCts, _cts);
        }

        /// <summary>获取关联的取消令牌。</summary>
        public CancellationToken Token => _cts.Token;

        /// <summary>异步获取跨进程构建锁。</summary>
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

            if (Volatile.Read(ref _owner._currentBuildCts) == _cts) {
                Volatile.Write(ref _owner._currentBuildCts, null);
            }

            _cts.Dispose();
            return default;
        }
    }

    /// <summary>
    /// 构建队列 Actor — 串行化命令处理，构建执行 fire-and-forget + Tell 回投。
    /// <para>SubmitCmd: 入队或立即执行。CancelCmd: 取消。BuildCompletedCmd: 处理结果 + 启动下一个。</para>
    /// </summary>
    private sealed class BuildQueueActor : ActorBase<IBuildQueueCommand, Unit> {
        private readonly BuildQueueService _owner;
        private readonly Queue<BuildQueueEntry> _pending = new();

        internal BuildQueueActor(BuildQueueService owner, ILogger? logger)
            : base(logger: logger) {
            _owner = owner;
        }

        protected override void Handle(IBuildQueueCommand cmd, CancellationToken ct) {
            switch (cmd) {
                case SubmitCmd submit:
                    HandleSubmit(submit.Entry);
                    break;
                case CancelCmd cancel:
                    HandleCancel(cancel);
                    break;
                case BuildCompletedCmd completed:
                    HandleBuildCompleted(completed);
                    break;
            }
        }

        private void HandleSubmit(BuildQueueEntry entry) {
            if (Volatile.Read(ref _owner._currentBuild) is not null) {
                _pending.Enqueue(entry);
                return;
            }
            StartBuild(entry);
        }

        private void StartBuild(BuildQueueEntry entry) {
            Volatile.Write(ref _owner._currentBuild, entry);
            entry.Status = BuildQueueEntryStatus.Building;
            entry.StartedAt = DateTimeOffset.UtcNow;

            _owner._logger?.LogInformation("Build {BuildId} started (checkpoint): queuePos={QueuePos}, pending={Pending}",
                entry.BuildId, entry.QueuePosition, _owner._store.GetAllEntries().Count(e => e.Status == BuildQueueEntryStatus.Queued));

            var task = Task.Run(async () => {
                BuildQueueResult? result = null;
                Exception? error = null;
                try {
                    result = await _owner.ExecuteBuildAsync(entry, CancellationToken.None).ConfigureAwait(false);
                } catch (Exception ex) {
                    error = ex;
                }
                Tell(new BuildCompletedCmd(entry, result, error));
            });
            RegisterInFlight(task);
        }

        private void HandleCancel(CancelCmd cancel) {
            if (!_owner._store.TryGetEntry(cancel.BuildId, out var entry)) {
                cancel.Tcs.TrySetResult(false);
                return;
            }

            switch (entry.Status) {
                case BuildQueueEntryStatus.Queued:
                    entry.Status = BuildQueueEntryStatus.Cancelled;
                    entry.CompletedAt = DateTimeOffset.UtcNow;
                    _owner.CompleteWithCancellation(cancel.BuildId, entry);
                    _owner._logger?.LogInformation("Build cancelled (was queued): {BuildId}", cancel.BuildId);
                    cancel.Tcs.TrySetResult(true);
                    break;

                case BuildQueueEntryStatus.Building:
                    entry.Status = BuildQueueEntryStatus.Cancelling;
                    Volatile.Read(ref _owner._currentBuildCts)?.Cancel();
                    _owner._logger?.LogInformation("Build cancelling (was building): {BuildId}", cancel.BuildId);
                    cancel.Tcs.TrySetResult(true);
                    break;

                default:
                    cancel.Tcs.TrySetResult(false);
                    break;
            }
        }

        private void HandleBuildCompleted(BuildCompletedCmd completed) {
            var entry = completed.Entry;

            if (completed.Error is OperationCanceledException && entry.Status == BuildQueueEntryStatus.Cancelling) {
                entry.Status = BuildQueueEntryStatus.Cancelled;
                entry.CompletedAt = DateTimeOffset.UtcNow;
                _owner.CompleteWithCancellation(entry.BuildId, entry);
                _owner._logger?.LogInformation("Build {BuildId} cancelled", entry.BuildId);
            } else if (completed.Error is not null) {
                entry.Status = BuildQueueEntryStatus.Failed;
                entry.CompletedAt = DateTimeOffset.UtcNow;

                var failResult = BuildQueueBase.CreateFailedResult(entry, completed.Error);
                entry.Result = failResult;

                if (_owner._store.TryGetTcs(entry.BuildId, out var failTcs)) {
                    failTcs.TrySetResult(failResult);
                }
                _owner._logger?.LogError(completed.Error, "Build {BuildId} failed with exception", entry.BuildId);
            } else if (completed.Result is not null) {
                var result = completed.Result;
                entry.Result = result;
                entry.CompletedAt = DateTimeOffset.UtcNow;

                entry.Status = result.Cancelled
                    ? BuildQueueEntryStatus.Cancelled
                    : result.ExitCode == 0
                        ? BuildQueueEntryStatus.Completed
                        : BuildQueueEntryStatus.Failed;

                var bufferKey = BuildResultBuffer.BuildBufferKey(entry.Request.Command, entry.Request.WorkingDirectory);
                _owner._resultBuffer.Add(bufferKey, result, entry.Request.WorkingDirectory);

                if (_owner._store.TryGetTcs(entry.BuildId, out var tcs)) {
                    tcs.TrySetResult(result);
                }
                _owner._logger?.LogInformation("Build {BuildId} completed: {Status}, exit={ExitCode}",
                    entry.BuildId, entry.Status, result.ExitCode);
            }

            Volatile.Write(ref _owner._currentBuild, null);

            _owner._logger?.LogDebug("Build {BuildId} checkpoint: status={Status}, remaining={Remaining}",
                entry.BuildId, entry.Status, _owner._store.GetAllEntries().Count(e => e.Status == BuildQueueEntryStatus.Queued));

            if (_pending.Count > 0) {
                StartBuild(_pending.Dequeue());
            }
        }
    }
}
