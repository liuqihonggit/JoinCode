namespace Core.Agents;

/// <summary>
/// 转录记录中间件 — 记录系统提示词和用户输入到 Transcript
/// 合并自路径 A 的 TranscriptMiddleware
/// 主代理 no-op
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware))]
public sealed partial class TranscriptMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public TranscriptMiddleware(IClockService clock, IAgentTranscriptService? transcriptService = null, ILogger<TranscriptMiddleware>? logger = null)
    {
        _clock = clock;
        _transcriptService = transcriptService;
        _logger = logger;
    }
    [Inject] private readonly IAgentTranscriptService? _transcriptService;
    [Inject] private readonly ILogger<TranscriptMiddleware>? _logger;
    [Inject] private readonly IClockService _clock;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (context.IsMainAgent || _transcriptService is null || context.Agent is null)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var userPrompt = context.SpawnOptions?.Prompt ?? context.SpawnOptions?.Description ?? context.Task;
        await AppendTranscriptEntryAsync(context.AgentId, "system", context.SystemPrompt, ct).ConfigureAwait(false);
        await AppendTranscriptEntryAsync(context.AgentId, "user", userPrompt, ct).ConfigureAwait(false);

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task AppendTranscriptEntryAsync(string agentId, string role, string content, CancellationToken cancellationToken)
    {
        try
        {
            await (_transcriptService ?? throw new InvalidOperationException("TranscriptService not available")).AppendEntryAsync("default", agentId, new TranscriptEntry
            {
                SessionId = "default",
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
