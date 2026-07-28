namespace Tools.Handlers;

/// <summary>
/// Agent 后台 Spawn 中间件 — 当 RunInBackground=true 时，使用 SpawnAgentAsync 启动后台代理
/// 对齐 TS: 后台模式 fire-and-forget
/// </summary>
[Register]
public sealed partial class AgentBackgroundSpawnMiddleware : IAgentToolMiddleware
{
    [Inject] private readonly IAgentService _agentService;
    [Inject] private readonly ITelemetryService? _telemetryService;

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
            AgentType = context.SubagentType,
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

        // 后台模式: 使用原有 SpawnAgentAsync（fire-and-forget）
        var agent = await _agentService.SpawnAgentAsync(spawnOptions, ct).ConfigureAwait(false);

        var response = new StringBuilder();
        response.AppendLine("Agent launched in background");
        response.AppendLine($"Agent ID: {agent.Id}");
        response.AppendLine($"Description: {agent.Description}");

        if (!string.IsNullOrEmpty(agent.AgentType))
        {
            response.AppendLine($"Type: {agent.AgentType}");
        }

        if (agent.IsolationMode != AgentIsolationMode.None)
        {
            response.AppendLine($"Isolation: {agent.IsolationMode}");
        }

        response.AppendLine();
        response.AppendLine("Status: async_launched");
        response.AppendLine("Agent is running in the background. You will be notified when it completes.");
        response.AppendLine($"Use {AgentToolName.AgentStatus.ToValue()} to query agent status, or {AgentToolName.AgentRunning.ToValue()} to see all running agents.");

        RecordAgentMetrics("spawn", true);
        context.BackgroundSpawnResult = ToolResultBuilder.Success()
            .WithText(response.ToString())
            .Build();
        context.Result = context.BackgroundSpawnResult;
        // 短路 — 后台模式不需要流式执行和 handoff
    }

    private void RecordAgentMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("agent.handler.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Agent handler count");
}
