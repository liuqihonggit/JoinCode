namespace Core.Agents;

/// <summary>
/// 转录记录中间件 — 记录系统提示词和用户输入到 Transcript
/// </summary>
[Register]
public sealed partial class TranscriptMiddleware : IAgentSpawnMiddleware
{
    [Inject] private readonly JoinCode.Abstractions.Interfaces.IAgentTranscriptService? _transcriptService;
    [Inject] private readonly ILogger<TranscriptMiddleware>? _logger;
    [Inject] private readonly IClockService _clock;

    /// <summary>转录记录在元数据保存之后</summary>

    /// <summary>转录记录失败不应中断管道</summary>
    public JoinCode.Abstractions.Pipeline.ErrorBehavior OnError => JoinCode.Abstractions.Pipeline.ErrorBehavior.Continue;

    public async Task InvokeAsync(AgentSpawnContext context, JoinCode.Abstractions.Pipeline.MiddlewareDelegate<AgentSpawnContext> next, CancellationToken ct)
    {
        if (_transcriptService is not null && context.SubAgent is not null)
        {
            await AppendTranscriptEntryAsync(context.SubAgent.Id, "system", context.SystemPrompt, ct).ConfigureAwait(false);
            await AppendTranscriptEntryAsync(context.SubAgent.Id, "user", context.Options.Prompt ?? context.Options.Description, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task AppendTranscriptEntryAsync(string agentId, string role, string content, CancellationToken cancellationToken)
    {
        try
        {
            await _transcriptService!.AppendEntryAsync("default", agentId, new TranscriptEntry
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
