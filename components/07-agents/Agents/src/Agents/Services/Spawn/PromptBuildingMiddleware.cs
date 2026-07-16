namespace Core.Agents;

/// <summary>
/// 提示构建中间件 — 构建系统提示词并加载记忆
/// </summary>
[Register]
public sealed partial class PromptBuildingMiddleware : IAgentSpawnMiddleware
{
    [Inject] private readonly JoinCode.Abstractions.Interfaces.IAgentPromptBuilder _promptBuilder;
    [Inject] private readonly IAgentMemoryService? _agentMemoryService;
    [Inject] private readonly ILogger<PromptBuildingMiddleware>? _logger;

    /// <summary>提示构建在定义解析之后</summary>

    /// <summary>提示构建失败应中断管道</summary>
    public JoinCode.Abstractions.Pipeline.ErrorBehavior OnError => JoinCode.Abstractions.Pipeline.ErrorBehavior.Propagate;

    public async Task InvokeAsync(AgentSpawnContext context, JoinCode.Abstractions.Pipeline.MiddlewareDelegate<AgentSpawnContext> next, CancellationToken ct)
    {
        var systemPrompt = await _promptBuilder.BuildSystemPromptAsync(
            context.Options.AgentType, context.Options.Description, cancellationToken: ct).ConfigureAwait(false);

        // 加载 Agent 记忆提示词 — 对齐 TS loadAgentMemoryPrompt
        var memoryScope = context.Options.MemoryScope ?? context.Definition?.Memory;
        if (memoryScope is not null && _agentMemoryService is not null && !string.IsNullOrWhiteSpace(context.Options.AgentType))
        {
            try
            {
                var memoryPrompt = await _agentMemoryService.LoadAgentMemoryPromptAsync(
                    context.Options.AgentType, memoryScope.Value, ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(memoryPrompt))
                    systemPrompt = $"{systemPrompt}\n\n{memoryPrompt}";

                // 检查快照并初始化 — 对齐 TS initializeAgentMemorySnapshots
                var snapshotCheck = await _agentMemoryService.CheckSnapshotAsync(
                    context.Options.AgentType, memoryScope.Value, ct).ConfigureAwait(false);

                if (snapshotCheck.Action == AgentMemorySnapshotAction.Initialize)
                {
                    await _agentMemoryService.InitializeFromSnapshotAsync(
                        context.Options.AgentType, memoryScope.Value, snapshotCheck.SnapshotTimestamp ?? throw new InvalidOperationException("SnapshotTimestamp is null"), ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[PromptBuildingMiddleware] 加载 Agent 记忆失败: {AgentType}", context.Options.AgentType);
            }
        }

        context.SystemPrompt = systemPrompt;

        await next(context, ct).ConfigureAwait(false);
    }
}
