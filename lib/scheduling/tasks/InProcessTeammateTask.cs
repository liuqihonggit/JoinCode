namespace Core.Scheduling.Tasks;

public interface IInProcessTeammateTaskExecutor
{
    Task<AgentTaskResult> ExecuteTeammateAsync(InProcessTeammateDefinition definition, CancellationToken ct = default);
    Task<bool> SendMessageToTeammateAsync(string teammateId, CoordinatorMessage message, CancellationToken ct = default);
    Task<IEnumerable<string>> GetActiveTeammatesAsync(CancellationToken ct = default);
    Task<IEnumerable<TeammateStateSnapshot>> GetActiveTeammateSnapshotsAsync(CancellationToken ct = default);
    Task StopTeammateAsync(string teammateId, CancellationToken ct = default);
    Task TerminateTeammateAsync(string teammateId, string? reason = null, CancellationToken ct = default);
    Task<bool> IsTeammateIdleAsync(string teammateId, CancellationToken ct = default);
    Task<bool> InterruptTeammateAsync(string teammateId, CancellationToken ct = default);
    event EventHandler<TeammateCompletedEventArgs>? TeammateCompleted;
}

/// <summary>
/// teammate 状态快照 — 供 GUI 渲染子会话树（含 ParentSessionId/Task/IsIdle 等，弥补 GetActiveTeammatesAsync 只返回 ID 的不足）。
/// </summary>
public sealed record TeammateStateSnapshot(
    string TeammateId,
    string? ParentSessionId,
    string Task,
    bool IsIdle,
    int TurnCount,
    string? LastResult);

/// <summary>
/// teammate 完成事件参数 — teammate 正常完成或被终止时触发，供 GUI 移除子会话卡片/补足结果。
/// </summary>
public sealed class TeammateCompletedEventArgs : EventArgs
{
    public required string TeammateId { get; init; }
    public required string Task { get; init; }
    public string? Output { get; init; }
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public int TurnCount { get; init; }
}

public sealed partial class InProcessTeammateDefinition
{
    public required string TaskId { get; init; }
    public required string TeammateId { get; init; }
    public required string Task { get; init; }
    public string? SystemPrompt { get; init; }
    public string? AgentType { get; init; }
    public AgentRole Role { get; init; } = AgentRole.Executor;
    public ExecutorVariant? Variant { get; init; }
    public string? AdditionalInstructions { get; init; }
    public int MaxIterations { get; init; } = 50;
    public List<string> InitialContext { get; init; } = [];
    public string? TeamName { get; init; }
    public string? TeamId { get; init; }
    public string? ParentSessionId { get; init; }
    public string? Color { get; init; }
    public bool PlanModeRequired { get; init; }
    public bool ContinuousMode { get; init; }
    /// <summary>隔离模式 — Worktree 时 teammate 在独立工作树中执行（供 mainAgent 接手分析 diff）</summary>
    public AgentIsolationMode IsolationMode { get; init; } = AgentIsolationMode.None;
}

public sealed class TeammateState
{
    public required IAgent Agent { get; init; }
    public required CancellationTokenSource LifecycleCts { get; init; }
    public required TeammateContext Context { get; init; }
    public bool IsIdle { get; set; }
    public string? LastResult { get; set; }
    public int TurnCount { get; set; }
    /// <summary>
    /// 任务描述 — 来自 <see cref="InProcessTeammateDefinition.Task"/>，供 snapshot 暴露给 GUI 渲染子会话标题。
    /// </summary>
    public string Task { get; init; } = string.Empty;
    /// <summary>
    /// 当前 per-turn work 的 CTS — Interrupt 时只 cancel 此 CTS 中断当前 work，不杀 lifecycle。
    /// 由循环体在 work 开始前设置、结束后清空；InterruptTeammateAsync 读取并 cancel。
    /// 由 Actor Consumer 线程独占访问，无需锁。
    /// </summary>
    public CancellationTokenSource? CurrentWorkCts { get; set; }
}

/// <summary>
/// Teammate Actor 命令 — Channel 中的消息类型
/// </summary>
public interface ITeammateCommand;

internal sealed record RegisterTeammateCmd(
    string TeammateId,
    TeammateState State,
    Channel<CoordinatorMessage> PendingChannel,
    TaskCompletionSource Tcs) : ITeammateCommand;

internal sealed record UnregisterTeammateCmd(string TeammateId) : ITeammateCommand;

internal sealed record StopTeammateCmd(
    string TeammateId,
    TaskCompletionSource Tcs) : ITeammateCommand;

internal sealed record TryCleanupTeammateCmd(
    string TeammateId,
    TaskCompletionSource Tcs) : ITeammateCommand;

internal sealed record SetWorkCtsCmd(
    string TeammateId,
    CancellationTokenSource WorkCts,
    TaskCompletionSource Tcs) : ITeammateCommand;

internal sealed record ClearWorkCtsCmd(string TeammateId) : ITeammateCommand;

internal sealed record InterruptTeammateCmd(
    string TeammateId,
    TaskCompletionSource<bool> Tcs) : ITeammateCommand;

/// <summary>
/// 进程内 Teammate 任务执行器 — Actor 化：继承 ActorBase，Consumer 线程独占 _activeTeammates/_pendingMessages，
/// 消除 AsyncLock。复合操作（Stop+Cleanup、TryCleanup、Interrupt）通过 Actor 命令保证原子性。
/// 读操作（GetActiveTeammates、GetSnapshots、IsIdle）直接读 ConcurrentDictionary，无锁无命令开销。
/// 循环逻辑委托 TeammateLoopRunner，清理逻辑委托 TeammateCleanupHelper。
/// </summary>
[Register(typeof(IInProcessTeammateTaskExecutor), ServiceLifetime.Singleton)]
public sealed partial class InProcessTeammateTaskExecutor : ActorBase<ITeammateCommand, Unit>, IInProcessTeammateTaskExecutor, ITeammateRuntime
{
    private readonly IAgentLifecycleManager _agentLifecycleManager;
    private readonly IMailbox _messageBroker;
    private readonly ILogger<InProcessTeammateTaskExecutor>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;
    private readonly IMailboxPoller? _mailboxPoller;
    private readonly IPlanModeManager? _planModeManager;
    private readonly ConcurrentDictionary<string, TeammateState> _activeTeammates = new();
    private readonly ConcurrentDictionary<string, Channel<CoordinatorMessage>> _pendingMessages = new();
    private readonly MiddlewarePipeline<TeammateExecutionContext>? _executePipeline;
    private readonly IAgentWorktreeService? _worktreeService;
    private readonly IAgentWorktreeManager? _worktreeManager;
    private readonly TeammateCleanupHelper _cleanupHelper;
    private readonly TeammateLoopRunner _loopRunner;

    public event EventHandler<TeammateCompletedEventArgs>? TeammateCompleted;    public InProcessTeammateTaskExecutor(
        IAgentLifecycleManager agentLifecycleManager,
        IMailbox messageBroker,
        ILogger<InProcessTeammateTaskExecutor>? logger = null,
        ILoggerFactory? loggerFactory = null,
        ITelemetryService? telemetryService = null,
        IMailboxPoller? mailboxPoller = null,
        IPlanModeManager? planModeManager = null,
        IEnumerable<ITeammateExecutionMiddleware>? executeMiddlewares = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        IClockService? clock = null,
        IAgentWorktreeService? worktreeService = null,
        IAgentWorktreeManager? worktreeManager = null)
        : base()
    {
        _agentLifecycleManager = agentLifecycleManager;
        _messageBroker = messageBroker;
        _logger = logger;
        _telemetryService = telemetryService;
        _mailboxPoller = mailboxPoller;
        _planModeManager = planModeManager;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;
        _worktreeService = worktreeService;
        _worktreeManager = worktreeManager;

        _cleanupHelper = new TeammateCleanupHelper(
            agentLifecycleManager, messageBroker, mailboxPoller, worktreeManager, logger);

        _loopRunner = new TeammateLoopRunner(
            agentLifecycleManager, messageBroker, logger, telemetryService,
            planModeManager, _subAgentContextAccessor, this);

        if (executeMiddlewares is not null && loggerFactory is not null)
        {
            _executePipeline = new PipelineBuilder<TeammateExecutionContext>()
                .WithLoggingScope(loggerFactory)
                .UseRange(executeMiddlewares)
                .Build();
        }
        else if (executeMiddlewares is not null)
        {
            _executePipeline = new MiddlewarePipeline<TeammateExecutionContext>(executeMiddlewares);
        }
    }

    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    public async Task<AgentTaskResult> ExecuteTeammateAsync(InProcessTeammateDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (_executePipeline is not null)
        {
            return await ExecuteTeammateViaPipelineAsync(definition, ct).ConfigureAwait(false);
        }

        return await ExecuteTeammateDirectAsync(definition, ct).ConfigureAwait(false);
    }

    private async Task<AgentTaskResult> ExecuteTeammateViaPipelineAsync(InProcessTeammateDefinition definition, CancellationToken ct)
    {
        var ctx = new TeammateExecutionContext
        {
            Definition = definition,
            CancellationToken = ct,
            RunLoopAsync = (d, s, t) => { _loopRunner.RunTeammateLoopBackground(d, s, t); return Task.CompletedTask; },
            TryCleanupAsync = (teammateId) => ((ITeammateRuntime)this).TryCleanupTeammateAsync(teammateId),
            CleanupAsync = (teammateId, state) => _cleanupHelper.CleanupTeammateAsync(teammateId, state),
            ActiveTeammates = _activeTeammates,
            PendingMessages = _pendingMessages,
        };

        var pipeline = _executePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, ct).ConfigureAwait(false);
        }

        return ctx.Result ?? AgentTaskResult.Failure(definition.TaskId, definition.TeammateId, "Pipeline produced no result", 0);
    }

    private async Task<AgentTaskResult> ExecuteTeammateDirectAsync(InProcessTeammateDefinition definition, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var startTime = _clock.GetUtcNow();

        try
        {
            _logger?.LogInformation(L.T(StringKey.InProcessTeammateStartLog),
                definition.TeammateId, definition.Task, definition.ContinuousMode);

            await using var scope = new TeammateDirectScope(this, definition, ct);
            await scope.InitAsync().ConfigureAwait(false);
            var state = scope.State;

            if (definition.ContinuousMode)
            {
                _loopRunner.RunTeammateLoopBackground(definition, state, scope.LifecycleToken);
                scope.Detach();

                var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
                return AgentTaskResult.Success(definition.TaskId, definition.TeammateId, "Teammate started in continuous mode", elapsed);
            }

            if (definition.PlanModeRequired && _planModeManager != null && !_planModeManager.IsInPlanMode)
            {
                await TryEnterPlanModeIfNeededAsync(definition, ct).ConfigureAwait(false);
            }

            var result = await _agentLifecycleManager.ExecuteAsync(state.Agent, ct).ConfigureAwait(false);
            var elapsed2 = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;

            RecordTeammateMetrics("execute", result.IsSuccess);
            return result.IsSuccess
                ? AgentTaskResult.Success(definition.TaskId, definition.TeammateId, result.Output ?? string.Empty, elapsed2)
                : AgentTaskResult.Failure(definition.TaskId, definition.TeammateId, result.Error ?? "Teammate execution failed", elapsed2);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
            _logger?.LogError(ex, L.T(StringKey.InProcessTeammateFailedLog, definition.TeammateId));
            RecordTeammateMetrics("execute", false);
            return AgentTaskResult.Failure(definition.TaskId, definition.TeammateId, ex.Message, elapsed);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SendMessageToTeammateAsync(string teammateId, CoordinatorMessage message, CancellationToken ct = default)
    {
        if (_pendingMessages.TryGetValue(teammateId, out var channel))
        {
            await channel.Writer.WriteAsync(message, ct).ConfigureAwait(false);
        }

        return await _messageBroker.SendAsync(teammateId, message, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<string>> GetActiveTeammatesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IEnumerable<string>>(_activeTeammates.Keys);
    }

    /// <summary>
    /// 返回所有活跃 teammate 的状态快照 — 供 GUI 渲染子会话树（含 ParentSessionId/Task/IsIdle 等）。
    /// </summary>
    public Task<IEnumerable<TeammateStateSnapshot>> GetActiveTeammateSnapshotsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IEnumerable<TeammateStateSnapshot>>(
            _activeTeammates.Select(kv => new TeammateStateSnapshot(
                kv.Key, kv.Value.Context.ParentSessionId, kv.Value.Task,
                kv.Value.IsIdle, kv.Value.TurnCount, kv.Value.LastResult)).ToList());
    }

    /// <inheritdoc/>
    public async Task StopTeammateAsync(string teammateId, CancellationToken ct = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new StopTeammateCmd(teammateId, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task TerminateTeammateAsync(string teammateId, string? reason = null, CancellationToken ct = default)
    {
        if (!_activeTeammates.TryGetValue(teammateId, out _))
        {
            return;
        }

        var shutdownMsg = new CoordinatorMessage
        {
            FromAgentId = "coordinator",
            ToAgentId = teammateId,
            MessageType = TeammateMessageType.ShutdownRequest.ToValue(),
            Content = reason ?? "Teammate shutdown requested"
        };

        await SendMessageToTeammateAsync(teammateId, shutdownMsg, ct).ConfigureAwait(false);

        _logger?.LogInformation("Shutdown request sent to Teammate {TeammateId}: {Reason}", teammateId, reason);
    }

    /// <inheritdoc/>
    public Task<bool> IsTeammateIdleAsync(string teammateId, CancellationToken ct = default)
    {
        return Task.FromResult(_activeTeammates.TryGetValue(teammateId, out var state) && state.IsIdle);
    }

    /// <summary>
    /// 中断 teammate 当前 per-turn work — 只 cancel CurrentWorkCts，
    /// 不 cancel lifecycle，teammate 进 idle 等待 next prompt（对齐 TS 原版 inProcessRunner ESC 行为）。
    /// 若 teammate 不存在或当前无活跃 work，返回 false。
    /// </summary>
    public async Task<bool> InterruptTeammateAsync(string teammateId, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(new InterruptTeammateCmd(teammateId, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Actor Consumer — 线程独占 _activeTeammates/_pendingMessages，串行处理命令，无需锁。
    /// </summary>
    protected override async ValueTask HandleAsync(ITeammateCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case RegisterTeammateCmd cmd:
                _activeTeammates[cmd.TeammateId] = cmd.State;
                _pendingMessages[cmd.TeammateId] = cmd.PendingChannel;
                cmd.Tcs.TrySetResult();
                break;

            case UnregisterTeammateCmd cmd:
                _activeTeammates.TryRemove(cmd.TeammateId, out _);
                break;

            case StopTeammateCmd cmd:
                if (_activeTeammates.TryRemove(cmd.TeammateId, out var stopState))
                {
                    await stopState.LifecycleCts.CancelAsync().ConfigureAwait(false);
                    _pendingMessages.TryRemove(cmd.TeammateId, out var stopChannel);
                    stopChannel?.Writer.Complete();
                    await _cleanupHelper.CleanupTeammateAsync(cmd.TeammateId, stopState).ConfigureAwait(false);
                }
                cmd.Tcs.TrySetResult();
                break;

            case TryCleanupTeammateCmd cmd:
                if (_activeTeammates.TryRemove(cmd.TeammateId, out var cleanupState))
                {
                    _pendingMessages.TryRemove(cmd.TeammateId, out var cleanupChannel);
                    cleanupChannel?.Writer.Complete();
                    await _cleanupHelper.CleanupTeammateAsync(cmd.TeammateId, cleanupState).ConfigureAwait(false);
                }
                cmd.Tcs.TrySetResult();
                break;

            case SetWorkCtsCmd cmd:
                if (_activeTeammates.TryGetValue(cmd.TeammateId, out var setState))
                {
                    setState.CurrentWorkCts = cmd.WorkCts;
                }
                cmd.Tcs.TrySetResult();
                break;

            case ClearWorkCtsCmd cmd:
                if (_activeTeammates.TryGetValue(cmd.TeammateId, out var clearState))
                {
                    clearState.CurrentWorkCts = null;
                }
                break;

            case InterruptTeammateCmd cmd:
                if (!_activeTeammates.TryGetValue(cmd.TeammateId, out var interruptState))
                {
                    cmd.Tcs.TrySetResult(false);
                    break;
                }
                var workCts = interruptState.CurrentWorkCts;
                if (workCts is null || workCts.IsCancellationRequested)
                {
                    cmd.Tcs.TrySetResult(false);
                    break;
                }
                await workCts.CancelAsync().ConfigureAwait(false);
                _logger?.LogInformation("Teammate {TeammateId} 当前 work 已中断（interrupt），进入 idle 等待 next prompt", cmd.TeammateId);
                cmd.Tcs.TrySetResult(true);
                break;
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogWarning(ex, "Teammate Actor Consumer 命令处理异常");
    }

    // --- ITeammateRuntime 实现 ---

    async Task ITeammateRuntime.SetCurrentWorkCtsAsync(string teammateId, CancellationTokenSource workCts, CancellationToken lifecycleCt)
    {
        var tcs = CreateTcs();
        await SendAsync(new SetWorkCtsCmd(teammateId, workCts, tcs), lifecycleCt).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    async Task ITeammateRuntime.ClearCurrentWorkCtsAsync(string teammateId)
    {
        await SendAsync(new ClearWorkCtsCmd(teammateId), CancellationToken.None).ConfigureAwait(false);
    }

    async Task ITeammateRuntime.TryCleanupTeammateAsync(string teammateId)
    {
        try
        {
            var tcs = CreateTcs();
            await SendAsync(new TryCleanupTeammateCmd(teammateId, tcs), CancellationToken.None).ConfigureAwait(false);
            await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.CleanupTeammateAttemptFailedLog, teammateId));
        }
    }

    Channel<CoordinatorMessage>? ITeammateRuntime.GetPendingChannel(string teammateId)
    {
        _pendingMessages.TryGetValue(teammateId, out var channel);
        return channel;
    }

    void ITeammateRuntime.RecordTeammateMetrics(string operation, bool isSuccess)
    {
        _telemetryService?.RecordCount("scheduling.teammate.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "In-process teammate execution count");
    }

    void ITeammateRuntime.OnTeammateCompleted(TeammateCompletedEventArgs args)
    {
        try
        {
            TeammateCompleted?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Teammate {TeammateId} TeammateCompleted event handler threw", args.TeammateId);
        }
    }

    // --- 辅助方法 ---

    private void RecordTeammateMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("scheduling.teammate.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "In-process teammate execution count");

    /// <summary>
    /// 尝试进入 plan mode — PlanModeRequired 且未已在 plan mode 时自动进入,失败仅警告不抛异常
    /// </summary>
    private async Task TryEnterPlanModeIfNeededAsync(InProcessTeammateDefinition definition, CancellationToken ct)
    {
        try
        {
            _logger?.LogInformation("Teammate {TeammateId} requires plan mode, entering automatically", definition.TeammateId);

            var planResult = await _planModeManager!.EnterPlanModeAsync(
                description: $"Teammate {definition.TeammateId}: {definition.Task}",
                cancellationToken: ct).ConfigureAwait(false);

            if (!planResult.Success)
            {
                _logger?.LogWarning("Teammate {TeammateId} failed to enter plan mode: {Error}", definition.TeammateId, planResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Teammate {TeammateId} failed to enter plan mode", definition.TeammateId);
        }
    }

    /// <summary>
    /// Teammate 直接执行作用域 — 封装 ExecuteTeammateDirectAsync 的资源获取/释放
    /// <para>InitAsync:spawn agent + worktree 隔离 + 注册 broker/polling + 创建 lifecycleCts + 构造 state + 发 Actor 命令注册到 _activeTeammates/_pendingMessages</para>
    /// <para>Detach:ContinuousMode 时调用,后台循环接管清理,DisposeAsync 仅发注销命令</para>
    /// <para>DisposeAsync:发注销命令 + CleanupTeammateAsync(或半成品清理),修复原资源泄漏 bug</para>
    /// </summary>
    private sealed class TeammateDirectScope : IAsyncDisposable
    {
        private readonly InProcessTeammateTaskExecutor _owner;
        private readonly InProcessTeammateDefinition _definition;
        private readonly CancellationToken _externalCt;
        private readonly string _teammateId;

        private IAgent? _agent;
        private bool _brokerRegistered;
        private CancellationTokenSource? _lifecycleCts;
        private TeammateState? _state;
        private Channel<CoordinatorMessage>? _pendingChannel;
        private bool _registered;
        private bool _detached;
        private int _disposed;

        /// <summary>teammate 状态 — InitAsync 后可用</summary>
        public TeammateState State => _state ?? throw new InvalidOperationException("Scope not initialized");

        /// <summary>lifecycle 取消令牌 — 传给 RunTeammateLoopBackground</summary>
        public CancellationToken LifecycleToken => _lifecycleCts?.Token ?? CancellationToken.None;

        public TeammateDirectScope(InProcessTeammateTaskExecutor owner, InProcessTeammateDefinition definition, CancellationToken externalCt)
        {
            _owner = owner;
            _definition = definition;
            _externalCt = externalCt;
            _teammateId = definition.TeammateId;
        }

        /// <summary>
        /// 异步初始化 — 获取所有资源(agent/worktree/broker/polling/lifecycleCts/state/Actor注册)
        /// 抛异常时 DisposeAsync 清理半成品
        /// </summary>
        public async Task InitAsync()
        {
            var options = new SubAgentOptions
            {
                Role = _definition.Role != default ? _definition.Role : AgentRole.Executor,
                Variant = _definition.Variant,
                AdditionalInstructions = _definition.AdditionalInstructions,
                MaxIterations = _definition.MaxIterations,
                ContentReplacementState = _owner._subAgentContextAccessor.Current?.ContentReplacementState?.Clone(),
                SessionId = _owner._subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId,
            };

            _agent = await _owner._agentLifecycleManager.SpawnSubAgentAsync(_definition.Task, options, _externalCt).ConfigureAwait(false);

            if (_definition.IsolationMode == AgentIsolationMode.Worktree)
            {
                try
                {
                    AgentWorktreeSession? wtSession = null;
                    if (_owner._worktreeManager is not null)
                    {
                        wtSession = await _owner._worktreeManager.CreateWorktreeForAgentAsync(_agent.ObjectId.UniqueId, cancellationToken: _externalCt).ConfigureAwait(false);
                    }
                    else if (_owner._worktreeService is not null)
                    {
                        var wtResult = await _owner._worktreeService.CreateAgentWorktreeAsync(_agent.ObjectId.UniqueId, cancellationToken: _externalCt).ConfigureAwait(false);
                        wtSession = wtResult.Success ? wtResult.Session : null;
                    }

                    if (wtSession is not null)
                    {
                        ((AgentBase)_agent).Options.WorktreePath = wtSession.WorktreePath;
                        ((AgentBase)_agent).Options.WorktreeBranch = wtSession.BranchName;
                        if (((AgentBase)_agent).Context is not null)
                        {
                            ((AgentBase)_agent).Context!.WorktreePath = wtSession.WorktreePath;
                        }
                        _owner._logger?.LogInformation("Teammate {TeammateId} worktree created: {Path}", _teammateId, wtSession.WorktreePath);
                    }
                    else
                    {
                        _owner._logger?.LogWarning("Teammate {TeammateId} worktree creation failed, degrading to normal mode", _teammateId);
                    }
                }
                catch (Exception ex)
                {
                    _owner._logger?.LogWarning(ex, "Teammate {TeammateId} worktree creation exception, degrading to normal mode", _teammateId);
                }
            }

            if (_definition.InitialContext is { Count: > 0 })
            {
                foreach (var ctx in _definition.InitialContext)
                {
                    ((AgentBase)_agent).AddContext(ctx);
                }
            }

            var sessionId = _definition.ParentSessionId ?? _owner._subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
            _owner._messageBroker.RegisterAgent(_teammateId, sessionId);
            _brokerRegistered = true;
            _owner._cleanupHelper.StartMailboxPollingIfNeeded(_teammateId);

            _lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(_externalCt);

            var teammateContext = new TeammateContext
            {
                AgentId = _teammateId,
                AgentName = _teammateId,
                TeamName = _definition.TeamName ?? "default",
                TeamId = _definition.TeamId,
                Color = _definition.Color,
                PlanModeRequired = _definition.PlanModeRequired,
                ParentSessionId = _definition.ParentSessionId ?? sessionId,
                IsInProcess = true
            };

            _state = new TeammateState
            {
                Agent = _agent,
                LifecycleCts = _lifecycleCts,
                Context = teammateContext,
                IsIdle = false,
                Task = _definition.Task
            };

            _pendingChannel = Channel.CreateUnbounded<CoordinatorMessage>();

            var registerTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _owner.SendAsync(new RegisterTeammateCmd(_teammateId, _state, _pendingChannel, registerTcs), _externalCt).ConfigureAwait(false);
            await registerTcs.Task.ConfigureAwait(false);
            _registered = true;
        }

        /// <summary>
        /// 分离作用域 — ContinuousMode 时调用,后台循环接管清理,DisposeAsync 仅发注销命令
        /// </summary>
        public void Detach() => _detached = true;

        public async ValueTask DisposeAsync()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

            if (_detached) return;

            if (_registered)
            {
                _owner.TrySend(new UnregisterTeammateCmd(_teammateId));
            }

            if (_state is not null)
            {
                await _owner._cleanupHelper.CleanupTeammateAsync(_teammateId, _state).ConfigureAwait(false);
                _pendingChannel?.Writer.TryComplete();
                return;
            }

            _pendingChannel?.Writer.TryComplete();
            _lifecycleCts?.Dispose();
            if (_brokerRegistered)
            {
                _owner._cleanupHelper.StopMailboxPollingIfNeeded(_teammateId);
                _owner._messageBroker.UnregisterAgent(_teammateId);
            }
            if (_agent is not null)
            {
                try
                {
                    await _owner._agentLifecycleManager.DisposeAgentAsync(_agent.ObjectId.UniqueId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _owner._logger?.LogWarning(ex, "清理 Teammate {TeammateId} 半成品 Agent 资源失败", _teammateId);
                }
            }
        }
    }
}
