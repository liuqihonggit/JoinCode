namespace Core.Agents;

/// <summary>
/// 元数据保存中间件 — 保存 Agent 元数据到 Transcript
/// 合并自路径 A 的 MetadataMiddleware
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware))]
public sealed partial class MetadataMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public MetadataMiddleware(IAgentTranscriptService? transcriptService = null, ILogger<MetadataMiddleware>? logger = null)
    {
        _transcriptService = transcriptService;
        _logger = logger;
    }
    private readonly IAgentTranscriptService? _transcriptService;
    private readonly ILogger<MetadataMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (_transcriptService is not null && context.Agent is not null)
        {
            await SaveAgentMetadataAsync(context.Agent, context.Definition, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task SaveAgentMetadataAsync(IAgent agent, JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? definition, CancellationToken cancellationToken)
    {
        try
        {
            var baseAgent = (AgentBase)agent;
            await (_transcriptService ?? throw new InvalidOperationException("TranscriptService not available")).SaveMetadataAsync("default", new AgentMetadata
            {
                AgentId = agent.ObjectId.UniqueId,
                AgentType = baseAgent.Options.Variant?.ToValue() ?? baseAgent.Options.Role.ToValue(),
                Description = agent.Task,
                WorktreePath = baseAgent.Options.WorktreePath,
                ModelName = definition?.ModelName ?? baseAgent.Options.ModelName,
                Status = AgentStatusConstants.Running
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[MetadataMiddleware] 保存代理元数据失败: {AgentId}", agent.ObjectId.UniqueId);
        }
    }
}
