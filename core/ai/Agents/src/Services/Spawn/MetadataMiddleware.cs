namespace Core.Agents;

/// <summary>
/// 元数据保存中间件 — 保存 Agent 元数据到 Transcript
/// </summary>
[Register]
public sealed partial class MetadataMiddleware : IAgentSpawnMiddleware
{
    [Inject] private readonly JoinCode.Abstractions.Interfaces.IAgentTranscriptService? _transcriptService;
    [Inject] private readonly ILogger<MetadataMiddleware>? _logger;

    /// <summary>元数据保存在 MCP 初始化之后</summary>

    /// <summary>元数据保存失败不应中断管道</summary>
    public JoinCode.Abstractions.Pipeline.ErrorBehavior OnError => JoinCode.Abstractions.Pipeline.ErrorBehavior.Continue;

    public async Task InvokeAsync(AgentSpawnContext context, JoinCode.Abstractions.Pipeline.MiddlewareDelegate<AgentSpawnContext> next, CancellationToken ct)
    {
        if (_transcriptService is not null && context.SubAgent is not null)
        {
            await SaveAgentMetadataAsync(context.SubAgent, context.Definition, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task SaveAgentMetadataAsync(ISubAgent subAgent, JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? definition, CancellationToken cancellationToken)
    {
        try
        {
            await (_transcriptService ?? throw new InvalidOperationException("TranscriptService not available")).SaveMetadataAsync("default", new JoinCode.Abstractions.Interfaces.AgentMetadata
            {
                AgentId = subAgent.Id,
                AgentType = subAgent.Options.AgentType,
                Description = subAgent.Task,
                WorktreePath = subAgent.Options.WorktreePath,
                ModelName = definition?.ModelName ?? subAgent.Options.ModelName,
                Status = AgentStatusConstants.Running
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[MetadataMiddleware] 保存代理元数据失败: {AgentId}", subAgent.Id);
        }
    }
}
