namespace Core.Agents;

/// <summary>
/// 元数据保存中间件 — 保存 Agent 元数据到 Transcript
/// 合并自路径 A 的 MetadataMiddleware
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware), ServiceLifetime.Singleton)]
public sealed partial class MetadataMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    /// <summary>
    /// 构造 MetadataMiddleware 实例，注入可选的 transcript 服务与日志器
    /// </summary>
    public MetadataMiddleware(IAgentTranscriptService? transcriptService = null, ILogger<MetadataMiddleware>? logger = null)
    {
        _transcriptService = transcriptService;
        _logger = logger;
    }
    private readonly IAgentTranscriptService? _transcriptService;
    private readonly ILogger<MetadataMiddleware>? _logger;

    /// <summary>中间件错误处理策略：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行元数据保存：代理已创建时将代理元数据持久化到 transcript 服务
    /// </summary>
    /// <param name="context">统一 Spawn 上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
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
            await (_transcriptService ?? throw new InvalidOperationException("TranscriptService not available")).SaveMetadataAsync(SubAgentContext.Current?.SessionId ?? SessionIdFactory.DefaultSessionId, new AgentMetadata
            {
                AgentId = agent.ObjectId.UniqueId,
                AgentType = baseAgent.Options.Variant?.ToValue() ?? baseAgent.Options.Role.ToValue(),
                Description = agent.Task,
                WorktreePath = baseAgent.Options.WorktreePath,
                ModelName = definition?.ModelName ?? baseAgent.Options.ModelName,
                Status = AgentStatusEnumConstants.Running
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[MetadataMiddleware] 保存代理元数据失败: {AgentId}", agent.ObjectId.UniqueId);
        }
    }
}
