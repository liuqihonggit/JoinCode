namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 管理器依赖项 — 聚合非管道服务，减少构造函数参数
/// </summary>
[Register(typeof(ForkManagerDependencies), ServiceLifetime.Singleton)]
public sealed record ForkManagerDependencies(
    IAgentLifecycleManager LifecycleManager,
    IMailbox MessageBroker,
    IAgentWorktreeManager? WorktreeManager = null,
    IMailboxPoller? MailboxPoller = null,
    ITelemetryService? TelemetryService = null);

/// <summary>
/// Fork 子代理管理器 Actor 版 — 单消费者 Channel + 命令模式,零锁。
/// <para>所有可变状态(_entries/_sharedCache)由 Consumer 线程独占访问,无需锁。</para>
/// <para>慢操作(管道执行/CancelAgent/后台Execute)在 Consumer 外执行,通过命令读写状态(方案B)。</para>
/// <para>_forkSemaphore 保留:(N,N) 并发限流器,不是状态锁。</para>
/// <para>已接管 IForkSubAgentManager 注册(原 ForkSubAgentManager 已归档至 .xxx/)。</para>
/// </summary>
[Register(typeof(IForkSubAgentManager), ServiceLifetime.Singleton)]
public sealed partial class ForkSubAgentManagerActor : ActorBase<ForkSubAgentManagerActor.IForkCommand, Unit>, IForkSubAgentManager, IAsyncDisposable, ISubAgentConcurrencyUpdater {
    /// <summary>
    /// Fork 条目 — 持有不可变身份(<see cref="ForkIdentity"/>)与可变运行时状态(<see cref="ForkRuntime"/>)。
    /// <para>身份字段创建时设定后不再变更;运行时字段由 Consumer 线程独占访问,执行过程中反复读写。</para>
    /// </summary>
    private sealed class ForkEntry {
        /// <summary>Fork 身份信息 — 创建时设定,生命周期内不可变</summary>
        public required ForkIdentity Identity { get; init; }

        /// <summary>Fork 运行时状态 — 执行过程中可变,由 Consumer 线程独占访问</summary>
        public ForkRuntime Runtime { get; } = new();
    }

    /// <summary>ForkEntry 快照 — 安全传递 entry 状态到 Consumer 外（不可变）</summary>
    private sealed record ForkEntrySnapshot(
        ForkState State,
        string? Result,
        string? AgentId,
        string ParentSessionId,
        CancellationTokenSource? Cts,
        DateTime CreatedAt);

    /// <summary>命令标记接口 — Consumer 串行处理</summary>
    public interface IForkCommand;

    private sealed record CalculateForkDepthQuery(string ParentSessionId, TaskCompletionSource<int> Tcs) : IForkCommand;
    private sealed record SetupForkEntryCmd(ForkOptions Options, string ForkId, DateTime CreatedAt, ForkContext Context) : IForkCommand;
    private sealed record RemoveForkEntryCmd(string ForkId) : IForkCommand;
    private sealed record SetForkStateCmd(string ForkId, ForkState State, string? Result) : IForkCommand;
    private sealed record SetForkAgentIdCmd(string ForkId, string? AgentId) : IForkCommand;
    private sealed record SetForkCtsCmd(string ForkId, CancellationTokenSource? Cts) : IForkCommand;
    private sealed record BuildForkResultQuery(string ForkId, TaskCompletionSource<ForkResult> Tcs) : IForkCommand;
    private sealed record GetForkEntrySnapshotQuery(string ForkId, TaskCompletionSource<ForkEntrySnapshot?> Tcs) : IForkCommand;
    private sealed record GetActiveForksQuery(TaskCompletionSource<IReadOnlyList<ForkSubAgent>> Tcs) : IForkCommand;
    private sealed record MergeForkQuery(string ForkId, TaskCompletionSource<ForkResult> Tcs) : IForkCommand;
    private sealed record CleanupAllCmd() : IForkCommand;

    private readonly MiddlewarePipeline<ForkContext> _pipeline;
    private readonly ForkManagerDependencies _deps;
    private readonly ILogger<ForkSubAgentManagerActor>? _logger;
    private readonly IClockService _clock;
    private readonly Dictionary<string, ForkEntry> _entries = [];
    private readonly Dictionary<string, Dictionary<string, string>> _sharedCache = [];
    private volatile AsyncLock? _forkSemaphore;
    private int _disposed;

    /// <summary>Fork 完成事件 — Fork 进入终态时触发（在 Consumer 外触发）</summary>
    public event EventHandler<ForkCompletedEventArgs>? ForkCompleted;

    /// <summary>
    /// 初始化 Fork 子智能体管理器 Actor
    /// </summary>
    /// <param name="pipeline">中间件管道</param>
    /// <param name="deps">Fork 管理器依赖项</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="clock">时钟服务</param>
    /// <param name="concurrencyOptions">子智能体并发选项</param>
    public ForkSubAgentManagerActor(
        MiddlewarePipeline<ForkContext> pipeline,
        ForkManagerDependencies deps,
        ILogger<ForkSubAgentManagerActor>? logger = null,
        IClockService? clock = null,
        SubAgentConcurrencyOptions? concurrencyOptions = null)
        : base() {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _deps = deps ?? throw new ArgumentNullException(nameof(deps));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;

        var maxForks = (concurrencyOptions ?? new SubAgentConcurrencyOptions()).MaxConcurrentForks;
        _forkSemaphore = maxForks > 0
            ? new AsyncLock(nameof(ForkSubAgentManagerActor) + ".Concurrency", maxForks, maxForks)
            : null;
    }

    /// <summary>
    /// 异步执行 Fork 操作：通过中间件管道创建子智能体，支持同步与后台模式。
    /// 慢操作(管道执行/后台Execute)在 Consumer 外执行，状态读写通过命令投递。
    /// </summary>
    /// <param name="options">Fork 选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Fork 结果</returns>
    public async Task<ForkResult> ForkAsync(ForkOptions options, CancellationToken ct = default) {
        var sem = _forkSemaphore;
        IDisposable? releaser = null;
        if (sem is not null) {
            try {
                releaser = await sem.TryLockAsync(ct).ConfigureAwait(false)
                    ?? throw new System.TimeoutException($"锁 '{sem.Name}' 等待超时");
            } catch (ObjectDisposedException) {
                sem = _forkSemaphore;
                if (sem is not null)
                    releaser = await sem.TryLockAsync(ct).ConfigureAwait(false)
                        ?? throw new System.TimeoutException($"锁 '{sem.Name}' 等待超时");
            }
        }
        var semaphoreTransferredToBackground = false;
        try {
            var forkDepth = await AskForkDepthAsync(options.ParentSessionId, ct).ConfigureAwait(false);

            var forkId = $"fork-{Guid.NewGuid():N}";
            var createdAt = _clock.GetUtcNow();

            var context = new ForkContext {
                Options = options,
                ForkId = forkId,
                CreatedAt = createdAt,
                ForkDepth = forkDepth,
                CancellationToken = ct
            };

            await SendAsync(new SetupForkEntryCmd(options, forkId, createdAt, context), ct).ConfigureAwait(false);

            try {
                await _pipeline.ExecuteAsync(context, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                await SendAsync(new SetForkStateCmd(forkId, ForkState.Cancelled, null), ct).ConfigureAwait(false);
                RecordForkMetrics("fork_cancelled", false);
                _logger?.LogInformation("Fork {ForkId} was cancelled", forkId);

                await FireForkCompletedAsync(forkId, options.TaskDescription, ct).ConfigureAwait(false);

                return await AskBuildForkResultAsync(forkId, ct).ConfigureAwait(false);
            } catch (Exception ex) {
                await SendAsync(new SetForkStateCmd(forkId, ForkState.Failed, ex.Message), ct).ConfigureAwait(false);
                RecordForkMetrics("fork_error", false);
                _logger?.LogError(ex, "Fork {ForkId} failed with exception", forkId);

                await FireForkCompletedAsync(forkId, options.TaskDescription, ct).ConfigureAwait(false);

                return await AskBuildForkResultAsync(forkId, ct).ConfigureAwait(false);
            }

            if (!context.IsValidated) {
                await SendAsync(new RemoveForkEntryCmd(forkId), ct).ConfigureAwait(false);
                return new ForkResult {
                    ForkId = context.ForkId,
                    State = ForkState.Failed,
                    Result = context.ValidationFailureReason
                };
            }

            if (context.Agent is not null) {
                await SendAsync(new SetForkAgentIdCmd(forkId, context.Agent.ObjectId.UniqueId), ct).ConfigureAwait(false);
            }

            if (context.IsBackground && context.Agent is not null) {
                var forkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await SendAsync(new SetForkCtsCmd(forkId, forkCts), ct).ConfigureAwait(false);

                var forkToken = forkCts.Token;
                semaphoreTransferredToBackground = true;
                var capturedReleaser = releaser;
                Func<Task> runWithReleaser = async () => {
                    using var r = capturedReleaser;
                    await RunBackgroundForkAsync(forkId, context.Agent, options.TaskDescription, options.EventChannel, forkToken)
                        .ConfigureAwait(false);
                };
                _ = runWithReleaser()
                    .WaitAsync(TimeSpan.FromSeconds(10), forkToken).ConfigureAwait(false);

                return new ForkResult {
                    ForkId = forkId,
                    State = ForkState.Running,
                    SharedCache = context.SharedCache
                };
            }

            await SendAsync(new SetForkStateCmd(forkId, context.FinalState, context.FinalResult), ct).ConfigureAwait(false);

            await FireForkCompletedAsync(forkId, options.TaskDescription, ct).ConfigureAwait(false);

            return await AskBuildForkResultAsync(forkId, ct).ConfigureAwait(false);
        } finally {
            if (!semaphoreTransferredToBackground) {
                releaser.DisposeSafe(_logger);
            }
        }
    }

    /// <summary>
    /// 异步获取所有活跃 Fork 子智能体列表
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>活跃 Fork 子智能体只读列表</returns>
    public async Task<IReadOnlyList<ForkSubAgent>> GetActiveForksAsync(CancellationToken ct = default) {
        var tcs = new TaskCompletionSource<IReadOnlyList<ForkSubAgent>>();
        await SendAsync(new GetActiveForksQuery(tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步合并指定 Fork 的共享缓存到父会话，要求 Fork 处于 Completed 状态
    /// </summary>
    /// <param name="forkId">Fork 标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>合并结果</returns>
    public async Task<ForkResult> MergeForkAsync(string forkId, CancellationToken ct = default) {
        var tcs = new TaskCompletionSource<ForkResult>();
        await SendAsync(new MergeForkQuery(forkId, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步取消指定 Fork，停止邮箱轮询并取消子智能体执行。
    /// 慢操作(CancelAgent)在 Consumer 外执行，状态读写通过命令投递。
    /// </summary>
    /// <param name="forkId">Fork 标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task CancelForkAsync(string forkId, CancellationToken ct = default) {
        var snapshot = await AskForkEntryAsync(forkId, ct).ConfigureAwait(false);
        if (snapshot is null || snapshot.State != ForkState.Running)
            return;

        if (snapshot.Cts is not null) {
            await snapshot.Cts.CancelAsync().ConfigureAwait(false);
            snapshot.Cts.Dispose();
        }
        await SendAsync(new SetForkCtsCmd(forkId, null), ct).ConfigureAwait(false);

        var agentIdToCancel = snapshot.AgentId;
        if (agentIdToCancel is not null) {
            await SendAsync(new SetForkAgentIdCmd(forkId, null), ct).ConfigureAwait(false);
            StopMailboxPollingIfNeeded(agentIdToCancel);
            await _deps.LifecycleManager.CancelAgentAsync(agentIdToCancel, ct).ConfigureAwait(false);
        }

        await SendAsync(new SetForkStateCmd(forkId, ForkState.Cancelled, null), ct).ConfigureAwait(false);

        _logger?.LogInformation("Fork {ForkId} cancelled", forkId);

        await FireForkCompletedAsync(forkId, string.Empty, ct).ConfigureAwait(false);
    }

    /// <summary>Consumer 命令处理 — 串行访问所有可变状态,无锁</summary>
    protected override ValueTask HandleAsync(IForkCommand command, CancellationToken ct) {
        switch (command) {
            case CalculateForkDepthQuery(var parentId, var tcs):
            tcs.SetResult(CalculateForkDepth(parentId));
            return ValueTask.CompletedTask;
            case SetupForkEntryCmd(var options, var forkId, var createdAt, var context):
            SetupForkEntry(options, forkId, createdAt, context);
            return ValueTask.CompletedTask;
            case RemoveForkEntryCmd(var forkId):
            _entries.Remove(forkId);
            _sharedCache.Remove(forkId);
            return ValueTask.CompletedTask;
            case SetForkStateCmd(var forkId, var state, var result):
            if (_entries.TryGetValue(forkId, out var stateEntry)) {
                stateEntry.Runtime.State = state;
                if (result is not null)
                    stateEntry.Runtime.Result = result;
            }
            return ValueTask.CompletedTask;
            case SetForkAgentIdCmd(var forkId, var agentId):
            if (_entries.TryGetValue(forkId, out var agentEntry))
                agentEntry.Runtime.AgentId = agentId;
            return ValueTask.CompletedTask;
            case SetForkCtsCmd(var forkId, var cts):
            if (_entries.TryGetValue(forkId, out var ctsEntry))
                ctsEntry.Runtime.Cts = cts;
            return ValueTask.CompletedTask;
            case BuildForkResultQuery(var forkId, var tcs):
            tcs.SetResult(BuildForkResult(forkId));
            return ValueTask.CompletedTask;
            case GetForkEntrySnapshotQuery(var forkId, var tcs):
            tcs.SetResult(GetForkEntrySnapshot(forkId));
            return ValueTask.CompletedTask;
            case GetActiveForksQuery(var tcs):
            tcs.SetResult(GetActiveForksList());
            return ValueTask.CompletedTask;
            case MergeForkQuery(var forkId, var tcs):
            tcs.SetResult(MergeFork(forkId));
            return ValueTask.CompletedTask;
            case CleanupAllCmd:
            CleanupAll();
            return ValueTask.CompletedTask;
            default:
            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc/>
    protected override void OnConsumerError(Exception ex) {
        _logger?.LogError(ex, "[ForkSubAgentManagerActor] Consumer 命令处理异常");
    }

    private int CalculateForkDepth(string parentSessionId) {
        var depth = 0;
        var current = parentSessionId;

        for (var i = 0; i < 100; i++) {
            if (!_entries.TryGetValue(current, out var entry)) break;
            depth++;
            current = entry.Identity.ParentSessionId;
        }

        return depth;
    }

    private void SetupForkEntry(ForkOptions options, string forkId, DateTime createdAt, ForkContext context) {
        if (options.ShareCache) {
            var parentCache = _sharedCache.GetValueOrDefault(options.ParentSessionId, []);
            _sharedCache[options.ParentSessionId] = parentCache;
            _sharedCache[forkId] = parentCache;
            context.SharedCache = parentCache;
        } else {
            var forkCache = new Dictionary<string, string>();
            _sharedCache[forkId] = forkCache;
            context.SharedCache = forkCache;
        }

        _entries[forkId] = new ForkEntry {
            Identity = new ForkIdentity(options.ParentSessionId, createdAt)
        };
    }

    private ForkResult BuildForkResult(string forkId) {
        var cache = _sharedCache.GetValueOrDefault(forkId, []);
        var entry = _entries.GetValueOrDefault(forkId);

        return new ForkResult {
            ForkId = forkId,
            State = entry?.Runtime.State ?? ForkState.Failed,
            Result = entry?.Runtime.Result,
            SharedCache = cache
        };
    }

    private ForkEntrySnapshot? GetForkEntrySnapshot(string forkId) {
        if (!_entries.TryGetValue(forkId, out var entry)) return null;
        return new ForkEntrySnapshot(
            entry.Runtime.State,
            entry.Runtime.Result,
            entry.Runtime.AgentId,
            entry.Identity.ParentSessionId,
            entry.Runtime.Cts,
            entry.Identity.CreatedAt);
    }

    private IReadOnlyList<ForkSubAgent> GetActiveForksList() {
        return _entries
            .Where(kvp => kvp.Value.Runtime.State == ForkState.Running
                       || kvp.Value.Runtime.State == ForkState.Completed
                       || kvp.Value.Runtime.State == ForkState.Failed)
            .Select(kvp => new ForkSubAgent {
                ForkId = kvp.Key,
                ParentSessionId = kvp.Value.Identity.ParentSessionId,
                State = kvp.Value.Runtime.State,
                CreatedAt = kvp.Value.Identity.CreatedAt,
                Result = kvp.Value.Runtime.Result
            })
            .ToList();
    }

    private ForkResult MergeFork(string forkId) {
        if (!_entries.TryGetValue(forkId, out var entry)) {
            return new ForkResult {
                ForkId = forkId,
                State = ForkState.Failed,
                Result = "Fork not found"
            };
        }

        if (entry.Runtime.State != ForkState.Completed) {
            return new ForkResult {
                ForkId = forkId,
                State = entry.Runtime.State,
                Result = $"Fork is in {entry.Runtime.State} state, cannot merge"
            };
        }

        var parentSessionId = entry.Identity.ParentSessionId;

        if (IsSharedCacheForFork(forkId, parentSessionId)) {
            var forkCache = _sharedCache.GetValueOrDefault(forkId, []);
            var parentCache = _sharedCache.GetValueOrDefault(parentSessionId, []);

            foreach (var kvp in forkCache) {
                parentCache[kvp.Key] = kvp.Value;
            }

            _sharedCache[parentSessionId] = parentCache;
        }

        entry.Runtime.State = ForkState.Merged;

        _logger?.LogInformation("Fork {ForkId} merged into parent {ParentSessionId}",
            forkId, parentSessionId);

        var cache = _sharedCache.GetValueOrDefault(forkId, []);
        var mergedEntry = _entries.GetValueOrDefault(forkId);

        return new ForkResult {
            ForkId = forkId,
            State = ForkState.Merged,
            Result = mergedEntry?.Runtime.Result,
            SharedCache = cache
        };
    }

    private bool IsSharedCacheForFork(string forkId, string parentSessionId) {
        if (!_sharedCache.TryGetValue(forkId, out var forkCache)) return false;
        if (!_sharedCache.TryGetValue(parentSessionId, out var parentCache)) return false;
        return ReferenceEquals(forkCache, parentCache);
    }

    private void CleanupAll() {
        var ctsEntries = _entries.Values
            .Select(e => e.Runtime.Cts)
            .OfType<CancellationTokenSource>()
            .ToList();
        foreach (var cts in ctsEntries) {
            cts.Cancel();
            cts.Dispose();
        }
        _entries.Clear();
        _sharedCache.Clear();
    }

    private async Task<int> AskForkDepthAsync(string parentSessionId, CancellationToken ct) {
        var tcs = new TaskCompletionSource<int>();
        await SendAsync(new CalculateForkDepthQuery(parentSessionId, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    private async Task<ForkResult> AskBuildForkResultAsync(string forkId, CancellationToken ct) {
        var tcs = new TaskCompletionSource<ForkResult>();
        await SendAsync(new BuildForkResultQuery(forkId, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    private async Task<ForkEntrySnapshot?> AskForkEntryAsync(string forkId, CancellationToken ct) {
        var tcs = new TaskCompletionSource<ForkEntrySnapshot?>();
        await SendAsync(new GetForkEntrySnapshotQuery(forkId, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    private async Task RunBackgroundForkAsync(string forkId, IAgent agent, string taskDescription,
        JoinCode.Abstractions.LLM.Chat.SubAgentEventChannel? eventChannel, CancellationToken cancellationToken) {
        async Task EmitFinishedAsync(bool success, string? output) {
            if (eventChannel is null)
                return;
            var snapshot = await AskForkEntryAsync(forkId, CancellationToken.None).ConfigureAwait(false);
            eventChannel.Emit(JoinCode.Abstractions.LLM.Chat.ChatStreamEvent.AgentFinished(
                snapshot?.AgentId ?? forkId,
                success: success,
                finalOutput: output));
        }

        try {
            var result = await _deps.LifecycleManager.ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess) {
                await SendAsync(new SetForkStateCmd(forkId, ForkState.Completed, result.Output), CancellationToken.None).ConfigureAwait(false);
                RecordForkMetrics("fork_background", true);
                _logger?.LogInformation("Background Fork {ForkId} completed successfully", forkId);
            } else {
                await SendAsync(new SetForkStateCmd(forkId, ForkState.Failed, result.Error ?? "Unknown error"), CancellationToken.None).ConfigureAwait(false);
                RecordForkMetrics("fork_background", false);
                _logger?.LogWarning("Background Fork {ForkId} failed: {Error}", forkId, result.Error);
            }

            await EmitFinishedAsync(result.IsSuccess, result.IsSuccess ? result.Output : result.Error).ConfigureAwait(false);
            await FireForkCompletedAsync(forkId, taskDescription, CancellationToken.None).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            await SendAsync(new SetForkStateCmd(forkId, ForkState.Cancelled, null), CancellationToken.None).ConfigureAwait(false);
            RecordForkMetrics("fork_background_cancelled", false);
            _logger?.LogInformation("Background Fork {ForkId} was cancelled", forkId);

            await EmitFinishedAsync(success: false, "已取消").ConfigureAwait(false);
            await FireForkCompletedAsync(forkId, taskDescription, CancellationToken.None).ConfigureAwait(false);
        } catch (Exception ex) {
            await SendAsync(new SetForkStateCmd(forkId, ForkState.Failed, ex.Message), CancellationToken.None).ConfigureAwait(false);
            RecordForkMetrics("fork_background_error", false);
            _logger?.LogError(ex, "Background Fork {ForkId} failed with exception", forkId);

            await EmitFinishedAsync(success: false, ex.Message).ConfigureAwait(false);
            await FireForkCompletedAsync(forkId, taskDescription, CancellationToken.None).ConfigureAwait(false);
        } finally {
            await SendAsync(new SetForkCtsCmd(forkId, null), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task FireForkCompletedAsync(string forkId, string taskDescription, CancellationToken ct) {
        try {
            var snapshot = await AskForkEntryAsync(forkId, ct).ConfigureAwait(false);
            if (snapshot is null) return;

            string? worktreePath = null;

            if (_deps.WorktreeManager != null && snapshot.AgentId is not null) {
                var session = await _deps.WorktreeManager.GetWorktreeSessionAsync(snapshot.AgentId).ConfigureAwait(false);
                worktreePath = session?.WorktreePath;
            }

            ForkCompleted?.Invoke(this, new ForkCompletedEventArgs {
                ForkId = forkId,
                State = snapshot.State,
                TaskDescription = taskDescription,
                Result = snapshot.Result,
                Error = snapshot.State == ForkState.Failed ? snapshot.Result : null,
                WorktreePath = worktreePath
            });
        } catch (Exception ex) {
            _logger?.LogError(ex, "Failed to fire ForkCompleted event for {ForkId}", forkId);
        }
    }

    private void RecordForkMetrics(string operation, bool isSuccess)
        => ToolTelemetryHelper.RecordToolCount(_deps.TelemetryService, "fork.operation.count", operation, isSuccess, "Fork operation count");

    private void StopMailboxPollingIfNeeded(string agentId) {
        if (_deps.MailboxPoller == null) return;

        try {
            var sessionId = _deps.MessageBroker.GetSessionId(agentId);
            if (sessionId is not null) {
                _deps.MailboxPoller.StopPolling(agentId, sessionId);
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to stop mailbox polling for agent {AgentId}", agentId);
        }
    }

    /// <summary>
    /// 热重载 fork 并发上限 — 原子替换 SemaphoreSlim，旧的 Dispose（ADR 0048）
    /// </summary>
    public void UpdateConcurrencyOptions(SubAgentConcurrencyOptions options) {
        var maxForks = options.MaxConcurrentForks;
        var newSem = maxForks > 0
            ? new AsyncLock(nameof(ForkSubAgentManagerActor) + ".Concurrency", maxForks, maxForks)
            : null;
        var old = Interlocked.Exchange(ref _forkSemaphore, newSem);
        old?.Dispose();
        _logger?.LogInformation("fork 并发上限已热重载为 {Limit}", maxForks);
    }

    /// <summary>
    /// 异步释放管理器资源 — Tell 模式(发消息即走,不等待 Consumer 处理)
    /// </summary>
    /// <remarks>
    /// <para>⚠️ 死锁教训(2026-09-16):此方法曾用 Ask 模式(SendAsync + await cleanupTcs.Task 阻塞等待 Consumer 回复)。</para>
    /// <para>线程池饥饿时 Consumer 无法调度 → cleanupTcs.SetResult() 永不调用 → 永久阻塞死锁。</para>
    /// <para><b>根因</b>:Dispose 路径禁止用 Ask 模式(await tcs.Task),必须用 Tell 模式(TrySend 发消息即走)。</para>
    /// <para><b>Actor 模型原则</b>:Tell(发消息即走,fire-and-forget) vs Ask(发消息等回复,阻塞当前线程)。</para>
    /// <para>Dispose 是单向通知,不需要回复 — Consumer 会在退出前按 FIFO 处理完 channel 内剩余命令(含 CleanupAllCmd)。</para>
    /// <para><b>规则</b>:Dispose/DisposeAsync 路径禁止出现 await tcs.Task / await xxx.Task.ConfigureAwait,违者由分析器锁死。</para>
    /// </remarks>
    /// <returns>表示异步操作的任务</returns>
    public override ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        TrySend(new CleanupAllCmd());
        _forkSemaphore?.Dispose();
        return base.DisposeAsync();
    }
}