namespace Core.Scheduling.Tasks;

/// <summary>
/// 进程内 Teammate 任务执行器接口 — 提供 teammate 的执行、消息通信、状态查询、停止/终止/中断能力。
/// </summary>
public interface IInProcessTeammateTaskExecutor {
    /// <summary>
    /// 异步执行一个 teammate 任务。
    /// </summary>
    /// <param name="definition">teammate 定义,包含任务描述、Agent 配置与执行选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>teammate 任务执行结果。</returns>
    Task<AgentTaskResult> ExecuteTeammateAsync(InProcessTeammateDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// 异步向指定 teammate 发送协调消息。
    /// </summary>
    /// <param name="teammateId">teammate 唯一标识。</param>
    /// <param name="message">协调消息。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否成功投递到消息总线。</returns>
    Task<bool> SendMessageToTeammateAsync(string teammateId, CoordinatorMessage message, CancellationToken ct = default);

    /// <summary>
    /// 异步获取所有活跃 teammate 的标识列表。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>活跃 teammate 标识集合。</returns>
    Task<IEnumerable<string>> GetActiveTeammatesAsync(CancellationToken ct = default);

    /// <summary>
    /// 异步获取所有活跃 teammate 的状态快照,供 GUI 渲染子会话树。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>活跃 teammate 状态快照集合。</returns>
    Task<IEnumerable<TeammateStateSnapshot>> GetActiveTeammateSnapshotsAsync(CancellationToken ct = default);

    /// <summary>
    /// 异步停止指定 teammate — 取消生命周期并清理资源。
    /// </summary>
    /// <param name="teammateId">teammate 唯一标识。</param>
    /// <param name="ct">取消令牌。</param>
    Task StopTeammateAsync(string teammateId, CancellationToken ct = default);

    /// <summary>
    /// 异步终止指定 teammate — 发送 ShutdownRequest 消息,teammate 自行退出。
    /// </summary>
    /// <param name="teammateId">teammate 唯一标识。</param>
    /// <param name="reason">终止原因,可选。</param>
    /// <param name="ct">取消令牌。</param>
    Task TerminateTeammateAsync(string teammateId, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// 异步查询指定 teammate 是否处于空闲状态。
    /// </summary>
    /// <param name="teammateId">teammate 唯一标识。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否空闲。</returns>
    Task<bool> IsTeammateIdleAsync(string teammateId, CancellationToken ct = default);

    /// <summary>
    /// 异步中断指定 teammate 的当前 per-turn work — 不杀生命周期,teammate 进入 idle 等待下一轮。
    /// </summary>
    /// <param name="teammateId">teammate 唯一标识。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否成功中断(不存在或无活跃 work 时返回 false)。</returns>
    Task<bool> InterruptTeammateAsync(string teammateId, CancellationToken ct = default);

    /// <summary>
    /// teammate 完成事件 — teammate 正常完成或被终止时触发。
    /// </summary>
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
public sealed class TeammateCompletedEventArgs : EventArgs {
    /// <summary>teammate 唯一标识。</summary>
    public required string TeammateId { get; init; }
    /// <summary>任务描述。</summary>
    public required string Task { get; init; }
    /// <summary>teammate 输出内容,可选。</summary>
    public string? Output { get; init; }
    /// <summary>是否成功完成。</summary>
    public bool IsSuccess { get; init; }
    /// <summary>错误信息,失败时填充。</summary>
    public string? Error { get; init; }
    /// <summary>对话轮次计数。</summary>
    public int TurnCount { get; init; }
}

/// <summary>
/// 进程内 Teammate 定义 — 描述一个 teammate 的任务、Agent 配置与执行选项。
/// </summary>
public sealed partial class InProcessTeammateDefinition {
    /// <summary>任务唯一标识。</summary>
    public required string TaskId { get; init; }
    /// <summary>teammate 唯一标识。</summary>
    public required string TeammateId { get; init; }
    /// <summary>任务描述。</summary>
    public required string Task { get; init; }
    /// <summary>系统提示词,可选,覆盖默认 Agent 系统提示。</summary>
    public string? SystemPrompt { get; init; }
    /// <summary>Agent 类型标识,可选。</summary>
    public string? AgentType { get; init; }
    /// <summary>Agent 角色,默认 Executor。</summary>
    public AgentRole Role { get; init; } = AgentRole.Executor;
    /// <summary>执行器变体,可选,用于细分 Agent 执行策略。</summary>
    public ExecutorVariant? Variant { get; init; }
    /// <summary>附加指令,可选,拼接到任务描述后。</summary>
    public string? AdditionalInstructions { get; init; }
    /// <summary>最大迭代次数,默认 50。</summary>
    public int MaxIterations { get; init; } = 50;
    /// <summary>初始上下文列表,可选,在 Agent 启动前注入。</summary>
    public List<string> InitialContext { get; init; } = [];
    /// <summary>团队名称,可选。</summary>
    public string? TeamName { get; init; }
    /// <summary>团队唯一标识,可选。</summary>
    public string? TeamId { get; init; }
    /// <summary>父会话标识,可选,用于关联子会话树。</summary>
    public string? ParentSessionId { get; init; }
    /// <summary>UI 显示颜色,可选。</summary>
    public string? Color { get; init; }
    /// <summary>是否要求 Plan 模式 — 为 true 时自动进入 Plan 模式。</summary>
    public bool PlanModeRequired { get; init; }
    /// <summary>是否连续模式 — 为 true 时后台循环执行,等待消息驱动。</summary>
    public bool ContinuousMode { get; init; }
    /// <summary>隔离模式 — Worktree 时 teammate 在独立工作树中执行（供 mainAgent 接手分析 diff）</summary>
    public AgentIsolationMode IsolationMode { get; init; } = AgentIsolationMode.None;
}

/// <summary>
/// teammate 运行时状态 — 持有 Agent 实例、生命周期 CTS、上下文与运行时计数。
/// </summary>
public sealed class TeammateState {
    /// <summary>Agent 实例。</summary>
    public required IAgent Agent { get; init; }
    /// <summary>生命周期取消令牌源 — 控制 teammate 整体生命周期。</summary>
    public required CancellationTokenSource LifecycleCts { get; init; }
    /// <summary>teammate 元信息 — 团队、颜色、会话等元数据。</summary>
    public required TeammateMeta TeammateMeta { get; init; }
    /// <summary>是否空闲。</summary>
    public bool IsIdle { get; set; }
    /// <summary>最后一次结果,可选。</summary>
    public string? LastResult { get; set; }
    /// <summary>对话轮次计数。</summary>
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
/// 进程内 Teammate 任务执行器 — Actor 化：继承 ActorBase，Consumer 线程独占 _registry，
/// 消除 AsyncLock。复合操作（Stop+Cleanup、TryCleanup、Interrupt）通过 Actor 命令保证原子性。
/// 读操作（GetActiveTeammates、GetSnapshots、IsIdle）直接读 ConcurrentDictionary，无锁无命令开销。
/// 循环逻辑委托 TeammateLoopRunner，清理逻辑委托 TeammateCleanupHelper。
/// </summary>
[Register(typeof(IInProcessTeammateTaskExecutor), ServiceLifetime.Singleton)]
public sealed partial class InProcessTeammateTaskExecutor : ActorBase<ITeammateCommand, Unit>, IInProcessTeammateTaskExecutor, ITeammateRuntime {
    private readonly IAgentLifecycleManager _agentLifecycleManager;
    private readonly IMailbox _messageBroker;
    private readonly ILogger<InProcessTeammateTaskExecutor>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;
    private readonly IMailboxPoller? _mailboxPoller;
    private readonly IPlanModeManager? _planModeManager;
    private readonly TeammateRegistry _registry = new();
    private readonly MiddlewarePipeline<TeammateExecutionContext>? _executePipeline;
    private readonly IAgentWorktreeService? _worktreeService;
    private readonly IAgentWorktreeManager? _worktreeManager;
    private readonly TeammateCleanupHelper _cleanupHelper;
    private readonly TeammateLoopRunner _loopRunner;

    /// <summary>
    /// teammate 完成事件 — teammate 正常完成或被终止时触发。
    /// </summary>
    public event EventHandler<TeammateCompletedEventArgs>? TeammateCompleted;

    /// <summary>
    /// 构造进程内 Teammate 任务执行器。
    /// </summary>
    /// <param name="agentLifecycleManager">Agent 生命周期管理器,用于 spawn/执行/销毁 Agent。</param>
    /// <param name="messageBroker">消息总线,用于 teammate 间通信。</param>
    /// <param name="logger">日志记录器,可选。</param>
    /// <param name="loggerFactory">日志工厂,可选,用于构建中间件管道日志作用域。</param>
    /// <param name="telemetryService">遥测服务,可选,用于记录执行指标。</param>
    /// <param name="mailboxPoller">邮箱轮询器,可选,用于启动消息轮询。</param>
    /// <param name="planModeManager">Plan 模式管理器,可选,PlanModeRequired 时自动进入 Plan 模式。</param>
    /// <param name="executeMiddlewares">执行中间件集合,可选,构建执行管道。</param>
    /// <param name="subAgentContextAccessor">子 Agent 上下文访问器,可选,默认使用 SubAgentContextAccessor。</param>
    /// <param name="clock">时钟服务,可选,默认使用系统时钟。</param>
    /// <param name="worktreeService">Agent 工作树服务,可选,Worktree 隔离模式时使用。</param>
    /// <param name="worktreeManager">Agent 工作树管理器,可选,优先于 worktreeService 使用。</param>
    public InProcessTeammateTaskExecutor(
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
        : base() {
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

        if (executeMiddlewares is not null && loggerFactory is not null) {
            _executePipeline = new PipelineBuilder<TeammateExecutionContext>()
                .WithLoggingScope(loggerFactory)
                .UseRange(executeMiddlewares)
                .Build();
        } else if (executeMiddlewares is not null) {
            _executePipeline = new MiddlewarePipeline<TeammateExecutionContext>(executeMiddlewares);
        }
    }


    /// <inheritdoc/>
    public async Task<AgentTaskResult> ExecuteTeammateAsync(InProcessTeammateDefinition definition, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(definition);

        if (_executePipeline is not null) {
            return await ExecuteTeammateViaPipelineAsync(definition, ct).ConfigureAwait(false);
        }

        return await ExecuteTeammateDirectAsync(definition, ct).ConfigureAwait(false);
    }

    private async Task<AgentTaskResult> ExecuteTeammateViaPipelineAsync(InProcessTeammateDefinition definition, CancellationToken ct) {
        var ctx = new TeammateExecutionContext {
            Definition = definition,
            CancellationToken = ct,
            RunLoopAsync = (d, s, t) => { _loopRunner.RunTeammateLoopBackground(d, s, t); return Task.CompletedTask; },
            TryCleanupAsync = (teammateId) => ((ITeammateRuntime)this).TryCleanupTeammateAsync(teammateId),
            CleanupAsync = (teammateId, state) => _cleanupHelper.CleanupTeammateAsync(teammateId, state),
            ActiveTeammates = _registry.ActiveTeammates,
            PendingMessages = _registry.PendingMessages,
        };

        var pipeline = _executePipeline;
        if (pipeline is not null) {
            await pipeline.ExecuteAsync(ctx, ct).ConfigureAwait(false);
        }

        return ctx.Result ?? AgentTaskResult.Failure(definition.TaskId, definition.TeammateId, "Pipeline produced no result", 0);
    }

    private async Task<AgentTaskResult> ExecuteTeammateDirectAsync(InProcessTeammateDefinition definition, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(definition);

        var startTime = _clock.GetUtcNow();

        try {
            _logger?.LogInformation(L.T(StringKey.InProcessTeammateStartLog),
                definition.TeammateId, definition.Task, definition.ContinuousMode);

            await using var scope = new TeammateDirectScope(this, definition, ct);
            await scope.InitAsync().ConfigureAwait(false);
            var state = scope.State;

            if (definition.ContinuousMode) {
                _loopRunner.RunTeammateLoopBackground(definition, state, scope.LifecycleToken);
                scope.Detach();

                var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
                return AgentTaskResult.Success(definition.TaskId, definition.TeammateId, "Teammate started in continuous mode", elapsed);
            }

            if (definition.PlanModeRequired && _planModeManager != null && !_planModeManager.IsInPlanMode) {
                await TryEnterPlanModeIfNeededAsync(definition, ct).ConfigureAwait(false);
            }

            var result = await _agentLifecycleManager.ExecuteAsync(state.Agent, ct).ConfigureAwait(false);
            var elapsed2 = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;

            RecordTeammateMetrics("execute", result.IsSuccess);
            return result.IsSuccess
                ? AgentTaskResult.Success(definition.TaskId, definition.TeammateId, result.Output ?? string.Empty, elapsed2)
                : AgentTaskResult.Failure(definition.TaskId, definition.TeammateId, result.Error ?? "Teammate execution failed", elapsed2);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
            _logger?.LogError(ex, L.T(StringKey.InProcessTeammateFailedLog, definition.TeammateId));
            RecordTeammateMetrics("execute", false);
            return AgentTaskResult.Failure(definition.TaskId, definition.TeammateId, ex.Message, elapsed);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SendMessageToTeammateAsync(string teammateId, CoordinatorMessage message, CancellationToken ct = default) {
        if (_registry.TryGetChannel(teammateId, out var channel)) {
            await channel.Writer.WriteAsync(message, ct).ConfigureAwait(false);
        }

        return await _messageBroker.SendAsync(teammateId, message, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<string>> GetActiveTeammatesAsync(CancellationToken ct = default) {
        return Task.FromResult<IEnumerable<string>>(_registry.GetAllTeammateIds());
    }

    /// <summary>
    /// 返回所有活跃 teammate 的状态快照 — 供 GUI 渲染子会话树（含 ParentSessionId/Task/IsIdle 等）。
    /// </summary>
    public Task<IEnumerable<TeammateStateSnapshot>> GetActiveTeammateSnapshotsAsync(CancellationToken ct = default) {
        return Task.FromResult<IEnumerable<TeammateStateSnapshot>>(_registry.GetSnapshots());
    }

    /// <inheritdoc/>
    public async Task StopTeammateAsync(string teammateId, CancellationToken ct = default) {
        var tcs = TcsFactory.Create();
        await SendAsync(new StopTeammateCmd(teammateId, tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task TerminateTeammateAsync(string teammateId, string? reason = null, CancellationToken ct = default) {
        if (!_registry.Contains(teammateId)) {
            return;
        }

        var shutdownMsg = new CoordinatorMessage {
            FromAgentId = "coordinator",
            ToAgentId = teammateId,
            MessageType = TeammateMessageType.ShutdownRequest.ToValue(),
            Content = reason ?? "Teammate shutdown requested"
        };

        await SendMessageToTeammateAsync(teammateId, shutdownMsg, ct).ConfigureAwait(false);

        _logger?.LogInformation("Shutdown request sent to Teammate {TeammateId}: {Reason}", teammateId, reason);
    }

    /// <inheritdoc/>
    public Task<bool> IsTeammateIdleAsync(string teammateId, CancellationToken ct = default) {
        return Task.FromResult(_registry.TryGetState(teammateId, out var state) && state.IsIdle);
    }

    /// <summary>
    /// 中断 teammate 当前 per-turn work — 只 cancel CurrentWorkCts，
    /// 不 cancel lifecycle，teammate 进 idle 等待 next prompt（对齐 TS 原版 inProcessRunner ESC 行为）。
    /// 若 teammate 不存在或当前无活跃 work，返回 false。
    /// </summary>
    public async Task<bool> InterruptTeammateAsync(string teammateId, CancellationToken ct = default) {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(new InterruptTeammateCmd(teammateId, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Actor Consumer — 线程独占 _registry，串行处理命令，无需锁。
    /// </summary>
    protected override async ValueTask HandleAsync(ITeammateCommand command, CancellationToken ct) {
        switch (command) {
            case RegisterTeammateCmd cmd:
            _registry.Register(cmd.TeammateId, cmd.State, cmd.PendingChannel);
            cmd.Tcs.TrySetResult();
            break;

            case UnregisterTeammateCmd cmd:
            _registry.Unregister(cmd.TeammateId);
            break;

            case StopTeammateCmd cmd:
            if (_registry.TryRemove(cmd.TeammateId, out var stopState, out var stopChannel)) {
                await stopState.LifecycleCts.CancelAsync().ConfigureAwait(false);
                stopChannel?.Writer.Complete();
                await _cleanupHelper.CleanupTeammateAsync(cmd.TeammateId, stopState).ConfigureAwait(false);
            }
            cmd.Tcs.TrySetResult();
            break;

            case TryCleanupTeammateCmd cmd:
            if (_registry.TryRemove(cmd.TeammateId, out var cleanupState, out var cleanupChannel)) {
                cleanupChannel?.Writer.Complete();
                await _cleanupHelper.CleanupTeammateAsync(cmd.TeammateId, cleanupState).ConfigureAwait(false);
            }
            cmd.Tcs.TrySetResult();
            break;

            case SetWorkCtsCmd cmd:
            if (_registry.TryGetState(cmd.TeammateId, out var setState)) {
                setState.CurrentWorkCts = cmd.WorkCts;
            }
            cmd.Tcs.TrySetResult();
            break;

            case ClearWorkCtsCmd cmd:
            if (_registry.TryGetState(cmd.TeammateId, out var clearState)) {
                clearState.CurrentWorkCts = null;
            }
            break;

            case InterruptTeammateCmd cmd:
            if (!_registry.TryGetState(cmd.TeammateId, out var interruptState)) {
                cmd.Tcs.TrySetResult(false);
                break;
            }
            var workCts = interruptState.CurrentWorkCts;
            if (workCts is null || workCts.IsCancellationRequested) {
                cmd.Tcs.TrySetResult(false);
                break;
            }
            await workCts.CancelAsync().ConfigureAwait(false);
            _logger?.LogInformation("Teammate {TeammateId} 当前 work 已中断（interrupt），进入 idle 等待 next prompt", cmd.TeammateId);
            cmd.Tcs.TrySetResult(true);
            break;
        }
    }

    /// <summary>命令消费者发生异常时的回调处理，记录警告日志。</summary>
    /// <param name="ex">消费者抛出的异常。</param>
    protected override void OnConsumerError(Exception ex) {
        _logger?.LogWarning(ex, "Teammate Actor Consumer 命令处理异常");
    }

    // --- ITeammateRuntime 实现 ---

    async Task ITeammateRuntime.SetCurrentWorkCtsAsync(string teammateId, CancellationTokenSource workCts, CancellationToken lifecycleCt) {
        var tcs = TcsFactory.Create();
        await SendAsync(new SetWorkCtsCmd(teammateId, workCts, tcs), lifecycleCt).ConfigureAwait(false);
        await AskAwait(tcs, lifecycleCt).ConfigureAwait(false);
    }

    async Task ITeammateRuntime.ClearCurrentWorkCtsAsync(string teammateId) {
        await SendAsync(new ClearWorkCtsCmd(teammateId), CancellationToken.None).ConfigureAwait(false);
    }

    async Task ITeammateRuntime.TryCleanupTeammateAsync(string teammateId) {
        try {
            var tcs = TcsFactory.Create();
            await SendAsync(new TryCleanupTeammateCmd(teammateId, tcs), CancellationToken.None).ConfigureAwait(false);
            await AskAwait(tcs, CancellationToken.None).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, L.T(StringKey.CleanupTeammateAttemptFailedLog, teammateId));
        }
    }

    Channel<CoordinatorMessage>? ITeammateRuntime.GetPendingChannel(string teammateId) {
        _registry.TryGetChannel(teammateId, out var channel);
        return channel;
    }

    void ITeammateRuntime.RecordTeammateMetrics(string operation, bool isSuccess) {
        ToolTelemetryHelper.RecordToolCount(_telemetryService, "scheduling.teammate.count", operation, isSuccess, "In-process teammate execution count");
    }

    void ITeammateRuntime.OnTeammateCompleted(TeammateCompletedEventArgs args) {
        try {
            TeammateCompleted?.Invoke(this, args);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Teammate {TeammateId} TeammateCompleted event handler threw", args.TeammateId);
        }
    }

    // --- 辅助方法 ---

    private void RecordTeammateMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("scheduling.teammate.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "In-process teammate execution count");

    /// <summary>
    /// 尝试进入 plan mode — PlanModeRequired 且未已在 plan mode 时自动进入,失败仅警告不抛异常
    /// </summary>
    private async Task TryEnterPlanModeIfNeededAsync(InProcessTeammateDefinition definition, CancellationToken ct) {
        try {
            _logger?.LogInformation("Teammate {TeammateId} requires plan mode, entering automatically", definition.TeammateId);

            var planResult = await _planModeManager!.EnterPlanModeAsync(
                description: $"Teammate {definition.TeammateId}: {definition.Task}",
                cancellationToken: ct).ConfigureAwait(false);

            if (!planResult.Success) {
                _logger?.LogWarning("Teammate {TeammateId} failed to enter plan mode: {Error}", definition.TeammateId, planResult.ErrorMessage);
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Teammate {TeammateId} failed to enter plan mode", definition.TeammateId);
        }
    }

    /// <summary>
    /// Teammate 直接执行作用域 — 封装 ExecuteTeammateDirectAsync 的资源获取/释放
    /// <para>InitAsync:spawn agent + worktree 隔离 + 注册 broker/polling + 创建 lifecycleCts + 构造 state + 发 Actor 命令注册到 _registry</para>
    /// <para>Detach:ContinuousMode 时调用,后台循环接管清理,DisposeAsync 仅发注销命令</para>
    /// <para>DisposeAsync:发注销命令 + CleanupTeammateAsync(或半成品清理),修复原资源泄漏 bug</para>
    /// </summary>
    private sealed class TeammateDirectScope : IAsyncDisposable {
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

        /// <summary>构造 Teammate 直接作用域。</summary>
        /// <param name="owner">所属执行器。</param>
        /// <param name="definition">Teammate 定义。</param>
        /// <param name="externalCt">外部取消令牌。</param>
        public TeammateDirectScope(InProcessTeammateTaskExecutor owner, InProcessTeammateDefinition definition, CancellationToken externalCt) {
            _owner = owner;
            _definition = definition;
            _externalCt = externalCt;
            _teammateId = definition.TeammateId;
        }

        /// <summary>
        /// 异步初始化 — 获取所有资源(agent/worktree/broker/polling/lifecycleCts/state/Actor注册)
        /// 抛异常时 DisposeAsync 清理半成品
        /// </summary>
        public async Task InitAsync() {
            var options = new SubAgentOptions {
                Role = _definition.Role != default ? _definition.Role : AgentRole.Executor,
                Variant = _definition.Variant,
                AdditionalInstructions = _definition.AdditionalInstructions,
                MaxIterations = _definition.MaxIterations,
                ContentReplacementState = _owner._subAgentContextAccessor.Current?.ContentReplacementState?.Clone(),
                SessionId = _owner._subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId,
            };

            _agent = await _owner._agentLifecycleManager.SpawnSubAgentAsync(_definition.Task, options, _externalCt).ConfigureAwait(false);

            if (_definition.IsolationMode == AgentIsolationMode.Worktree) {
                try {
                    AgentWorktreeSession? wtSession = null;
                    if (_owner._worktreeManager is not null) {
                        wtSession = await _owner._worktreeManager.CreateWorktreeForAgentAsync(_agent.ObjectId.UniqueId, cancellationToken: _externalCt).ConfigureAwait(false);
                    } else if (_owner._worktreeService is not null) {
                        var wtResult = await _owner._worktreeService.CreateAgentWorktreeAsync(_agent.ObjectId.UniqueId, cancellationToken: _externalCt).ConfigureAwait(false);
                        wtSession = wtResult.Success ? wtResult.Session : null;
                    }

                    if (wtSession is not null)
                        ApplyWorktreeSession(wtSession);
                    else
                        _owner._logger?.LogWarning("Teammate {TeammateId} worktree creation failed, degrading to normal mode", _teammateId);
                } catch (Exception ex) {
                    _owner._logger?.LogWarning(ex, "Teammate {TeammateId} worktree creation exception, degrading to normal mode", _teammateId);
                }
            }

            if (_definition.InitialContext is { Count: > 0 }) {
                foreach (var ctx in _definition.InitialContext) {
                    ((AgentBase)_agent).AddContext(ctx);
                }
            }

            var sessionId = _definition.ParentSessionId ?? _owner._subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
            _owner._messageBroker.RegisterAgent(_teammateId, sessionId);
            _brokerRegistered = true;
            _owner._cleanupHelper.StartMailboxPollingIfNeeded(_teammateId);

            _lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(_externalCt);

            var teammateMeta = new TeammateMeta {
                AgentName = _teammateId,
                TeamName = _definition.TeamName ?? "default",
                Color = _definition.Color,
                PlanModeRequired = _definition.PlanModeRequired,
                ParentSessionId = _definition.ParentSessionId ?? sessionId,
                IsInProcess = true
            };

            _state = new TeammateState {
                Agent = _agent,
                LifecycleCts = _lifecycleCts,
                TeammateMeta = teammateMeta,
                IsIdle = false,
                Task = _definition.Task
            };

            _pendingChannel = Channel.CreateBounded<CoordinatorMessage>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait });

            var registerTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _owner.SendAsync(new RegisterTeammateCmd(_teammateId, _state, _pendingChannel, registerTcs), _externalCt).ConfigureAwait(false);
            await _owner.AskAwait(registerTcs, _externalCt).ConfigureAwait(false);
            _registered = true;
        }

        /// <summary>
        /// 应用 worktree 会话到 Agent
        /// </summary>
        private void ApplyWorktreeSession(AgentWorktreeSession wtSession) {
            if (_agent is not AgentBase agent) return;
            agent.Options.WorktreePath = wtSession.WorktreePath;
            agent.Options.WorktreeBranch = wtSession.BranchName;
            if (agent.Context is not null)
                agent.Context.WorktreePath = wtSession.WorktreePath;
            _owner._logger?.LogInformation("Teammate {TeammateId} worktree created: {Path}", _teammateId, wtSession.WorktreePath);
        }

        /// <summary>
        /// 分离作用域 — ContinuousMode 时调用,后台循环接管清理,DisposeAsync 仅发注销命令
        /// </summary>
        public void Detach() => _detached = true;

        /// <summary>异步释放资源。</summary>
        public async ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            if (_detached) return;

            if (_registered) {
                _owner.TrySend(new UnregisterTeammateCmd(_teammateId));
            }

            if (_state is not null) {
                await _owner._cleanupHelper.CleanupTeammateAsync(_teammateId, _state).ConfigureAwait(false);
                _pendingChannel?.Writer.TryComplete();
                return;
            }

            _pendingChannel?.Writer.TryComplete();
            _lifecycleCts?.Dispose();
            if (_brokerRegistered) {
                _owner._cleanupHelper.StopMailboxPollingIfNeeded(_teammateId);
                _owner._messageBroker.UnregisterAgent(_teammateId);
            }
            if (_agent is not null) {
                try {
                    await _owner._agentLifecycleManager.DisposeAgentAsync(_agent.ObjectId.UniqueId, CancellationToken.None).ConfigureAwait(false);
                } catch (Exception ex) {
                    _owner._logger?.LogWarning(ex, "清理 Teammate {TeammateId} 半成品 Agent 资源失败", _teammateId);
                }
            }
        }
    }
}