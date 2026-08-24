namespace Tools.Handlers;

/// <summary>
/// Agent 后台 Spawn 中间件 — 当 RunInBackground=true 时，使用 SpawnAgentAsync 启动后台代理
/// 对齐 TS: 后台模式 fire-and-forget
/// </summary>
[Register(typeof(IAgentToolMiddleware), ServiceLifetime.Singleton)]
public sealed partial class AgentBackgroundSpawnMiddleware : ServiceEntity, IAgentToolMiddleware
{

    public AgentBackgroundSpawnMiddleware(IAgentService agentService, ITelemetryService? telemetryService = null)
    {
        _agentService = agentService;
        _telemetryService = telemetryService;
    }
    private readonly IAgentService _agentService;
    private readonly ITelemetryService? _telemetryService;

    /// <inheritdoc />
    public int Order => 300;

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(AgentToolContext context, MiddlewareDelegate<AgentToolContext> next, CancellationToken ct)
    {
        var spawnOptions = new AgentSpawnOptions
        {
            Description = context.Description,
            Prompt = context.Prompt,
            Role = context.SubagentRole,
            Variant = context.SubagentVariant,
            RunInBackground = context.RunInBackground ?? false,
            IsolationMode = AgentIsolationModeExtensions.FromValue(context.Isolation) ?? AgentIsolationMode.None,
            MemoryScope = AgentMemoryScopeExtensions.FromValue(context.Memory),
            Model = context.Model,
            Name = context.Name,
            Cwd = context.Cwd
        };

        if (!spawnOptions.RunInBackground)
        {
            // 非后台模式，将 spawnOptions 存入上下文供后续中间件使用
            context.SpawnOptions = spawnOptions;
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 后台模式: 限制工具集为异步白名单 — 对齐 claude code ASYNC_AGENT_ALLOWED_TOOLS
        spawnOptions = spawnOptions with { AllowedTools = AsyncAgentAllowedTools.Tools };

        // 后台模式: 使用原有 SpawnAgentAsync（fire-and-forget）
        var agent = await _agentService.SpawnAgentAsync(spawnOptions, ct).ConfigureAwait(false);

        var response = new StringBuilder();
        response.AppendLine("Agent launched in background");
        response.AppendLine($"Agent ID: {agent.Id}");
        response.AppendLine($"Description: {agent.Description}");

        if (agent.Role != default || agent.Variant.HasValue)
        {
            response.AppendLine($"Type: {agent.Variant?.ToValue() ?? agent.Role.ToValue()}");
        }

        if (agent.IsolationMode != AgentIsolationMode.None)
        {
            response.AppendLine($"Isolation: {agent.IsolationMode}");
        }

        response.AppendLine();
        response.AppendLine("Status: async_launched");
        response.AppendLine("Agent is running in the background. You will be notified when it completes.");
        response.AppendLine($"Use {AgentToolName.AgentStatus.ToValue()} to query agent status, or {AgentToolName.AgentRunning.ToValue()} to see all running agents.");

        ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "spawn", true);
        context.BackgroundSpawnResult = ToolResultBuilder.Success()
            .WithText(response.ToString())
            .Build();
        context.Result = context.BackgroundSpawnResult;
        // 短路 — 后台模式不需要流式执行和 handoff
    }

}
