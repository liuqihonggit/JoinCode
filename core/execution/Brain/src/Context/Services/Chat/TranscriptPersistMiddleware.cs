using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Configuration;

namespace Core.Context;

/// <summary>
/// Transcript 持久化中间件 — 对话流结束后把 ChatHistory 快照差量增量写入 transcript JSONL。
/// 落盘责任收敛：此前由三端各自手写（CLI=CliSession 手动 AppendEntries、GUI=GuiSessionStore
/// 全量覆盖、TUI 无持久化），下沉到引擎管道后三端自动获得统一增量语义。
/// OnError=Continue：落盘失败不影响对话继续执行。
/// </summary>
[Register]
public sealed partial class TranscriptPersistMiddleware : ServiceEntity, IChatMiddleware
{
    [Inject] private readonly ITranscriptService? _transcriptService;
    [Inject] private readonly IChatContextManager _contextManager;
    [Inject] private readonly ILogger<TranscriptPersistMiddleware>? _logger;

    public TranscriptPersistMiddleware(
        ITranscriptService? transcriptService,
        IChatContextManager contextManager,
        ILogger<TranscriptPersistMiddleware>? logger = null)
    {
        _transcriptService = transcriptService;
        _contextManager = contextManager;
        _logger = logger;
    }

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 透传下游事件 → 下游完成后取快照差量写 transcript。
    /// 不缓冲事件流，保证流式响应实时性（对齐 SaveContextMiddleware 模式）。
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var startCount = _contextManager.CurrentMessageCount;

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }

        if (context.IsDryRun || _transcriptService is null)
            yield break;

        // Worker 进程不写主 session 文件 — 对齐原 CliSession.AppendTranscriptEntriesAsync 守卫
        var agentRole = Environment.GetEnvironmentVariable(JccEnvVar.AgentRole.ToValue());
        if (string.Equals(agentRole, "worker", StringComparison.OrdinalIgnoreCase))
            yield break;

        var snapshot = await _contextManager.GetMessageListAsync(ct).ConfigureAwait(false);
        if (snapshot.Count <= startCount)
            yield break;

        var entries = new List<TranscriptEntry>(snapshot.Count - startCount);
        for (var i = startCount; i < snapshot.Count; i++)
        {
            var message = snapshot[i];
            if (string.IsNullOrEmpty(message.Content))
                continue;
            entries.Add(new TranscriptEntry
            {
                Role = message.Role.ToValue(),
                Content = message.Content,
                Timestamp = DateTime.UtcNow,
                ModelId = context.FinalModelId,
            });
        }

        if (entries.Count == 0)
            yield break;

        try
        {
            await _transcriptService.AppendEntriesAsync(_contextManager.SessionId, entries, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[TranscriptPersist] transcript 写入失败（会话 {SessionId}），本轮对话可能丢失", _contextManager.SessionId);
        }
    }
}
