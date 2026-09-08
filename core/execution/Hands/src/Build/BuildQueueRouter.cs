namespace Services.Build;

/// <summary>
/// 多 Worker 编译队列 — 用 RouterActor 管理多个 BuildWorker 并行编译。
/// <para>与 <see cref="BuildQueueService"/> 串行模式互补:多 Worker 模式适用于多仓库并行编译场景。</para>
/// <para>不修改现有 BuildQueueService,通过 DI 手动注册替换:services.AddSingleton&lt;IBuildQueueService, BuildQueueRouter&gt;()。</para>
/// <para>Worker 崩溃时 RouterActor 自动重启(OneForOne 策略),不影响其他 Worker。</para>
/// </summary>
public sealed class BuildQueueRouter : IBuildQueueService
{
    private readonly BuildQueueRouterActor _router;
    private readonly ConcurrentDictionary<string, BuildQueueEntry> _entries = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BuildQueueResult>> _waitHandles = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancelSources = new();
    private readonly ILogger<BuildQueueRouter>? _logger;
    private int _buildCounter;
    private int _disposed;

    /// <summary>
    /// 构造多 Worker 编译队列。
    /// </summary>
    /// <param name="actuatorRegistry">系统执行器注册表(获取 Bash 执行编译)</param>
    /// <param name="workerCount">Worker 数量(默认 2,建议等于 CPU 核心数)</param>
    /// <param name="preventSleepService">防睡眠服务(可选)</param>
    /// <param name="logger">日志(可选)</param>
    public BuildQueueRouter(
        ISystemActuatorRegistry actuatorRegistry,
        int workerCount = 2,
        IPreventSleepService? preventSleepService = null,
        ILogger<BuildQueueRouter>? logger = null)
    {
        _logger = logger;
        _router = new BuildQueueRouterActor();

        workerCount = Math.Max(1, workerCount);
        for (var i = 0; i < workerCount; i++)
        {
            var workerId = $"build-worker-{i}";
            _router.AddWorkerAsync(workerId, _ =>
            {
                var worker = new BuildWorker(
                    workerId, actuatorRegistry, preventSleepService,
                    logger as ILogger);
                return new ValueTask<IAsyncDisposable>(worker);
            }).AsTask().GetAwaiter().GetResult();
        }

        _logger?.LogInformation("BuildQueueRouter initialized with {WorkerCount} workers", workerCount);
    }

    /// <inheritdoc />
    public async Task<string> SubmitAsync(BuildRequest request, CancellationToken ct)
    {
        ThrowIfDisposed();

        var entry = new BuildQueueEntry
        {
            BuildId = $"b-{Interlocked.Increment(ref _buildCounter):D4}",
            Request = request,
            Status = BuildQueueEntryStatus.Queued,
            QueuePosition = _entries.Count
        };

        _entries[entry.BuildId] = entry;

        var tcs = new TaskCompletionSource<BuildQueueResult>();
        _waitHandles[entry.BuildId] = tcs;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _cancelSources[entry.BuildId] = cts;

        try
        {
            await _router.RouteAsync(
                new BuildWorker.ExecuteBuildCommand(entry, tcs, cts.Token),
                async (msg, worker) =>
                {
                    if (worker is BuildWorker w)
                        await w.SubmitAsync(msg).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
        catch
        {
            _waitHandles.TryRemove(entry.BuildId, out var failedTcs);
            failedTcs?.TrySetCanceled();
            _cancelSources.TryRemove(entry.BuildId, out var failedCts);
            failedCts?.Dispose();
            throw;
        }

        _logger?.LogInformation("Build submitted to router: {BuildId}, command: {Command}",
            entry.BuildId, request.Command);

        return entry.BuildId;
    }

    /// <inheritdoc />
#pragma warning disable VSTHRD003
    public Task<BuildQueueResult> WaitAsync(string buildId, CancellationToken ct)
    {
        if (!_waitHandles.TryGetValue(buildId, out var tcs))
            throw new InvalidOperationException($"Build {buildId} not found");
        return tcs.Task;
    }
#pragma warning restore VSTHRD003

    /// <inheritdoc />
    public Task<bool> CancelAsync(string buildId, CancellationToken ct)
    {
        if (!_entries.TryGetValue(buildId, out var entry))
            return Task.FromResult(false);

        switch (entry.Status)
        {
            case BuildQueueEntryStatus.Queued:
                entry.Status = BuildQueueEntryStatus.Cancelled;
                entry.CompletedAt = DateTimeOffset.UtcNow;
                CompleteWithCancellation(buildId, entry);
                _logger?.LogInformation("Build cancelled (was queued): {BuildId}", buildId);
                return Task.FromResult(true);

            case BuildQueueEntryStatus.Building:
                entry.Status = BuildQueueEntryStatus.Cancelling;
                if (_cancelSources.TryGetValue(buildId, out var cts))
                    cts.Cancel();
                _logger?.LogInformation("Build cancelling (was building): {BuildId}", buildId);
                return Task.FromResult(true);

            default:
                return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public BuildQueueEntry? GetBuild(string buildId)
    {
        return _entries.TryGetValue(buildId, out var entry) ? entry : null;
    }

    /// <inheritdoc />
    public BuildQueueStatus GetStatus()
    {
        var pendingCount = _entries.Values.Count(e => e.Status == BuildQueueEntryStatus.Queued);
        var buildingCount = _entries.Values.Count(e => e.Status == BuildQueueEntryStatus.Building);
        var currentBuild = _entries.Values.FirstOrDefault(e => e.Status == BuildQueueEntryStatus.Building);

        return new BuildQueueStatus
        {
            PendingCount = pendingCount,
            IsBuilding = buildingCount > 0,
            CurrentBuildId = currentBuild?.BuildId,
            CurrentBuildAgentId = currentBuild?.Request.AgentId,
            RecentBuilds = _entries.Values
                .OrderByDescending(e => e.Request.SubmittedAt)
                .Take(10)
                .ToList()
        };
    }

    /// <inheritdoc />
    public Task ClearCacheAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string GetOutputRange(string buildId, int startLine, int endLine)
    {
        var entry = _entries.TryGetValue(buildId, out var e) ? e : null;
        if (entry?.Result is null)
            return $"Build {buildId} not found or has no result";

        var output = entry.Result.ExitCode == 0
            ? entry.Result.Output
            : $"{entry.Result.ErrorOutput}\n{entry.Result.Output}";

        var lines = output.Split('\n');
        if (endLine <= 0) endLine = lines.Length;

        startLine = Math.Max(1, startLine);
        endLine = Math.Min(lines.Length, endLine);

        if (startLine > endLine)
            return $"Invalid range: start={startLine}, end={endLine}, total={lines.Length}";

        var selected = lines[(startLine - 1)..endLine];
        return string.Join('\n', selected);
    }

    private void CompleteWithCancellation(string buildId, BuildQueueEntry entry)
    {
        var result = new BuildQueueResult
        {
            BuildId = buildId,
            ExitCode = -1,
            Output = string.Empty,
            ErrorOutput = "Build was cancelled",
            WaitDuration = entry.StartedAt.HasValue
                ? entry.StartedAt.Value - entry.Request.SubmittedAt
                : TimeSpan.Zero,
            BuildDuration = TimeSpan.Zero,
            QueuePosition = entry.QueuePosition,
            Cancelled = true
        };
        entry.Result = result;

        _waitHandles.TryGetValue(buildId, out var tcs);
        tcs?.TrySetResult(result);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(BuildQueueRouter));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        foreach (var cts in _cancelSources.Values)
            cts.Cancel();

        await _router.DisposeAsync().ConfigureAwait(false);

        foreach (var tcs in _waitHandles.Values)
            tcs.TrySetCanceled();

        foreach (var cts in _cancelSources.Values)
            cts.Dispose();
        _cancelSources.Clear();
    }
}

/// <summary>RouterActor 暴露 public 构造函数</summary>
internal sealed class BuildQueueRouterActor : RouterActor<BuildWorker.ICommand>
{
    public BuildQueueRouterActor() : base() { }
}

/// <summary>
/// 编译事件 — 编译开始/完成/取消/失败事件,通过 OutputAsync 流输出。
/// </summary>
internal sealed record BuildEvent(string BuildId, string WorkerId, BuildQueueEntryStatus Status, string? Message = null);

/// <summary>
/// 编译 Worker — 单消费者 Actor,串行处理编译命令。
/// <para>每个 Worker 独立执行编译,不共享状态,崩溃时由 RouterActor 自动重启。</para>
/// <para>编译事件通过 OutputAsync 流输出。</para>
/// </summary>
internal sealed class BuildWorker : ActorBase<BuildWorker.ICommand, BuildEvent>
{
    internal interface ICommand;

    internal sealed record ExecuteBuildCommand(
        BuildQueueEntry Entry,
        TaskCompletionSource<BuildQueueResult> Tcs,
        CancellationToken BuildCt) : ICommand;

    private readonly string _workerId;
    private readonly ISystemActuatorRegistry _actuatorRegistry;
    private readonly IPreventSleepService? _preventSleepService;
    private readonly ILogger? _logger;

    public BuildWorker(
        string workerId,
        ISystemActuatorRegistry actuatorRegistry,
        IPreventSleepService? preventSleepService = null,
        ILogger? logger = null)
        : base(ActorBackpressure.Build)
    {
        _workerId = workerId;
        _actuatorRegistry = actuatorRegistry;
        _preventSleepService = preventSleepService;
        _logger = logger;
    }

    public ValueTask SubmitAsync(ICommand command) => SendAsync(command);

    protected override async ValueTask HandleAsync(ICommand command, CancellationToken ct)
    {
        if (command is not ExecuteBuildCommand(var entry, var tcs, var buildCt))
            return;

        entry.Status = BuildQueueEntryStatus.Building;
        entry.StartedAt = DateTimeOffset.UtcNow;

        TryPublish(new BuildEvent(entry.BuildId, _workerId, BuildQueueEntryStatus.Building));
        _logger?.LogInformation("[{WorkerId}] Build {BuildId} started: {Command}",
            _workerId, entry.BuildId, entry.Request.Command);

        try
        {
            var result = await ExecuteBuildAsync(entry, buildCt).ConfigureAwait(false);
            entry.Result = result;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            entry.Status = result.Cancelled
                ? BuildQueueEntryStatus.Cancelled
                : result.ExitCode == 0
                    ? BuildQueueEntryStatus.Completed
                    : BuildQueueEntryStatus.Failed;

            tcs.TrySetResult(result);
            TryPublish(new BuildEvent(entry.BuildId, _workerId, entry.Status, $"exit={result.ExitCode}"));
            _logger?.LogInformation("[{WorkerId}] Build {BuildId} completed: exit={ExitCode}",
                _workerId, entry.BuildId, result.ExitCode);
        }
        catch (OperationCanceledException)
        {
            entry.Status = BuildQueueEntryStatus.Cancelled;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            tcs.TrySetResult(CreateCancelledResult(entry));
            TryPublish(new BuildEvent(entry.BuildId, _workerId, BuildQueueEntryStatus.Cancelled));
            _logger?.LogInformation("[{WorkerId}] Build {BuildId} cancelled", _workerId, entry.BuildId);
        }
        catch (Exception ex)
        {
            entry.Status = BuildQueueEntryStatus.Failed;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            tcs.TrySetResult(CreateFailedResult(entry, ex));
            TryPublish(new BuildEvent(entry.BuildId, _workerId, BuildQueueEntryStatus.Failed, ex.Message));
            _logger?.LogError(ex, "[{WorkerId}] Build {BuildId} failed", _workerId, entry.BuildId);
        }
    }

    private async Task<BuildQueueResult> ExecuteBuildAsync(BuildQueueEntry entry, CancellationToken buildCt)
    {
        await (_preventSleepService?.PreventSleepAsync(cancellationToken: CancellationToken.None)
            ?? Task.CompletedTask).ConfigureAwait(false);
        try
        {
            var wallStart = DateTimeOffset.UtcNow;

            var result = await _actuatorRegistry.Get(SystemActuatorKind.Bash).ExecuteAsync(
                entry.Request.Command,
                workingDirectory: entry.Request.WorkingDirectory,
                cancellationToken: buildCt).ConfigureAwait(false);

            var wallElapsed = DateTimeOffset.UtcNow - wallStart;
            var sleepDetected = wallElapsed > result.ExecutionTime + TimeSpan.FromSeconds(30);

            return new BuildQueueResult
            {
                BuildId = entry.BuildId,
                ExitCode = result.ExitCode ?? -1,
                Output = result.Stdout ?? string.Empty,
                ErrorOutput = result.Stderr ?? string.Empty,
                WaitDuration = entry.StartedAt.HasValue
                    ? entry.StartedAt.Value - entry.Request.SubmittedAt
                    : TimeSpan.Zero,
                BuildDuration = result.ExecutionTime,
                QueuePosition = entry.QueuePosition,
                SleepDetected = sleepDetected,
                Cancelled = buildCt.IsCancellationRequested
            };
        }
        finally
        {
            await (_preventSleepService?.AllowSleepAsync(CancellationToken.None)
                ?? Task.CompletedTask).ConfigureAwait(false);
        }
    }

    private static BuildQueueResult CreateCancelledResult(BuildQueueEntry entry)
    {
        return new BuildQueueResult
        {
            BuildId = entry.BuildId,
            ExitCode = -1,
            Output = string.Empty,
            ErrorOutput = "Build was cancelled",
            WaitDuration = entry.StartedAt.HasValue
                ? entry.StartedAt.Value - entry.Request.SubmittedAt
                : TimeSpan.Zero,
            BuildDuration = TimeSpan.Zero,
            QueuePosition = entry.QueuePosition,
            Cancelled = true
        };
    }

    private static BuildQueueResult CreateFailedResult(BuildQueueEntry entry, Exception ex)
    {
        return new BuildQueueResult
        {
            BuildId = entry.BuildId,
            ExitCode = -1,
            Output = string.Empty,
            ErrorOutput = ex.Message,
            WaitDuration = entry.StartedAt.HasValue
                ? entry.StartedAt.Value - entry.Request.SubmittedAt
                : TimeSpan.Zero,
            BuildDuration = TimeSpan.Zero,
            QueuePosition = entry.QueuePosition
        };
    }
}
