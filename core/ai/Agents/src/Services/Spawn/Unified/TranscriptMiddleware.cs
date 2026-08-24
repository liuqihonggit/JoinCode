namespace Core.Agents;

/// <summary>
/// 转录记录中间件 — 记录系统提示词和用户输入到 Transcript
/// 合并自路径 A 的 TranscriptMiddleware
/// 主代理 no-op
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware))]
public sealed partial class TranscriptMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public TranscriptMiddleware(IClockService clock, IChatContextManager? contextManager = null, IAgentTranscriptService? transcriptService = null, ILogger<TranscriptMiddleware>? logger = null)
    {
        _clock = clock;
        _transcriptService = transcriptService;
        _contextManager = contextManager;
        _logger = logger;
    }
    private readonly IAgentTranscriptService? _transcriptService;
    private readonly IChatContextManager? _contextManager;
    private readonly ILogger<TranscriptMiddleware>? _logger;
    private readonly IClockService _clock;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (context.IsMainAgent || _transcriptService is null || context.Agent is null)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // T10：挂到当前引擎会话 — 此前写死 "default" 致子代理全部落入 default/subagents/，
        // 与主会话脱钩；现取 IChatContextManager.SessionId（引擎唯一数据源）
        var ownerSessionId = _contextManager?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
        var userPrompt = context.SpawnOptions?.Prompt ?? context.SpawnOptions?.Description ?? context.Task;
        await AppendTranscriptEntryAsync(ownerSessionId, context.AgentId, "system", context.SystemPrompt, ct).ConfigureAwait(false);
        await AppendTranscriptEntryAsync(ownerSessionId, context.AgentId, "user", userPrompt, ct).ConfigureAwait(false);

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task AppendTranscriptEntryAsync(string ownerSessionId, string agentId, string role, string content, CancellationToken cancellationToken)
    {
        try
        {
            await (_transcriptService ?? throw new InvalidOperationException("TranscriptService not available")).AppendEntryAsync(ownerSessionId, agentId, new TranscriptEntry
            {
                SessionId = ownerSessionId,
                Role = role,
                Content = content,
                Timestamp = _clock.GetUtcNow(),
                AgentId = agentId,
                IsSidechain = true
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[TranscriptMiddleware] 写入代理Transcript失败: {AgentId}", agentId);
        }
    }
}
