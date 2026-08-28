namespace Tools.Handlers;

/// <summary>
/// Agent Fork 判断中间件 — 当 subagent_type 为空且 ForkManager 可用时，走 fork 路径
/// 对齐 TS: 省略 subagent_type 时 fork 自己，继承完整对话上下文
/// </summary>
[Register(typeof(IAgentToolMiddleware), ServiceLifetime.Singleton)]
public sealed partial class AgentForkMiddleware : ServiceEntity, IAgentToolMiddleware
{

    public AgentForkMiddleware(ISubAgentContextAccessor subAgentContextAccessor, IForkSubAgentManager? forkManager = null, ITelemetryService? telemetryService = null, IInProcessTeammateTaskExecutor? teammateExecutor = null)
    {
        _subAgentContextAccessor = subAgentContextAccessor;
        _forkManager = forkManager;
        _telemetryService = telemetryService;
        _teammateExecutor = teammateExecutor;
    }
    private readonly IForkSubAgentManager? _forkManager;
    private readonly ITelemetryService? _telemetryService;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly IInProcessTeammateTaskExecutor? _teammateExecutor;

    /// <inheritdoc />
    public int Order => 200;

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(AgentToolContext context, MiddlewareDelegate<AgentToolContext> next, CancellationToken ct)
    {
        // SubagentType 非空 → 不走 fork/teammate，交给后续中间件
        if (!string.IsNullOrEmpty(context.SubagentType))
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 优先走 teammate 路径（支持 Interrupt 中断 + idle 恢复，对齐 TS 原版 inProcessRunner）
        if (_teammateExecutor is not null)
        {
            await ExecuteTeammatePathAsync(context, ct).ConfigureAwait(false);
            return;
        }

        // 回退 fork 路径
        if (_forkManager is not null)
        {
            await ExecuteForkPathAsync(context, ct).ConfigureAwait(false);
            return;
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task ExecuteTeammatePathAsync(AgentToolContext context, CancellationToken ct)
    {
        var teammateId = $"teammate-{Guid.NewGuid():N}";
        var sessionId = _subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;

        var definition = new InProcessTeammateDefinition
        {
            TaskId = teammateId,
            TeammateId = teammateId,
            Task = context.Prompt,
            ParentSessionId = sessionId,
            ContinuousMode = true,
            MaxIterations = 200
        };

        await _teammateExecutor!.ExecuteTeammateAsync(definition, ct).ConfigureAwait(false);

        JoinCode.Abstractions.LLM.Chat.SubAgentEventChannel.Current?.Emit(
            JoinCode.Abstractions.LLM.Chat.ChatStreamEvent.AgentStarted(
                teammateId,
                name: "teammate",
                description: context.Prompt,
                role: AgentRole.Coordinator.ToValue()));

        var response = new StringBuilder();
        response.AppendLine("Teammate sub-agent launched");
        response.AppendLine($"TeammateId: {teammateId}");
        response.AppendLine($"Instructions: {context.Prompt}");
        response.AppendLine();
        response.AppendLine("Status: async_launched");
        response.AppendLine("Teammate sub-agent is running in the background. You will be notified when it completes.");

        ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "teammate", true);
        context.ForkResult = ToolResultBuilder.Success()
            .WithText(response.ToString())
            .Build();
        context.Result = context.ForkResult;
    }

    private async Task ExecuteForkPathAsync(AgentToolContext context, CancellationToken ct)
    {
        var forkManager = _forkManager ?? throw new InvalidOperationException("ForkManager not available.");

        var sessionId = _subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
        var parentCacheSafeParams = _subAgentContextAccessor.Current?.CacheSafeParams;

        var forkOptions = new ForkOptions
        {
            ParentSessionId = sessionId,
            TaskDescription = context.Prompt,
            ShareCache = true,
            ShareContext = true,
            UseExactTools = true,
            RunInBackground = true,
            PermissionMode = PermissionMode.Plan,
            MaxIterations = 200,
            CacheSafeParams = parentCacheSafeParams,
            // 捕获当前回合的子代理事件通道 — 供后台完成时发射 AgentFinished 终态
            EventChannel = JoinCode.Abstractions.LLM.Chat.SubAgentEventChannel.Current
        };

        var result = await forkManager.ForkAsync(forkOptions, ct).ConfigureAwait(false);

        // 立即发射启动事件（此刻仍在回合作用域内，GUI 运行面板即刻出现该 fork 的卡片行）
        JoinCode.Abstractions.LLM.Chat.SubAgentEventChannel.Current?.Emit(
            JoinCode.Abstractions.LLM.Chat.ChatStreamEvent.AgentStarted(
                result.ForkId,
                name: "fork",
                description: context.Prompt,
                role: AgentRole.Coordinator.ToValue()));

        var response = new StringBuilder();
        response.AppendLine("Fork sub-agent launched");
        response.AppendLine($"ForkID: {result.ForkId}");
        response.AppendLine($"Instructions: {context.Prompt}");
        response.AppendLine();
        response.AppendLine("Status: async_launched");
        response.AppendLine("Fork sub-agent is running in the background. You will be notified when it completes.");
        response.AppendLine("Fork inherits the parent's full context and tool pool, sharing Prompt Cache.");

        ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "fork", true);
        context.ForkResult = ToolResultBuilder.Success()
            .WithText(response.ToString())
            .Build();
        context.Result = context.ForkResult;
    }

}
