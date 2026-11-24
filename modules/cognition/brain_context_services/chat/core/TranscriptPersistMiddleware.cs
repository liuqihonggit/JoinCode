namespace Core.Context;

/// <summary>
/// Transcript 持久化中间件 — 对话流结束后把 ChatHistory 快照差量增量写入 transcript JSONL。
/// 落盘责任收敛：此前由三端各自手写（CLI=CliSession 手动 AppendEntries、GUI=GuiSessionStore
/// 全量覆盖、TUI 无持久化），下沉到引擎管道后三端自动获得统一增量语义。
/// OnError=Continue：落盘失败不影响对话继续执行。
/// </summary>
[Register(typeof(IChatMiddleware), ServiceLifetime.Singleton)]
public sealed partial class TranscriptPersistMiddleware : ServiceEntity, IChatMiddleware {
    private readonly ITranscriptService? _transcriptService;
    private readonly IChatContextManager _contextManager;
    private readonly ILogger<TranscriptPersistMiddleware>? _logger;

    /// <summary>
    /// 初始化 Transcript 持久化中间件
    /// </summary>
    /// <param name="transcriptService">transcript 持久化服务（可选，null 时禁用持久化）</param>
    /// <param name="contextManager">聊天上下文管理器</param>
    /// <param name="logger">可选日志记录器</param>
    public TranscriptPersistMiddleware(
        ITranscriptService? transcriptService,
        IChatContextManager contextManager,
        ILogger<TranscriptPersistMiddleware>? logger = null) {
        _transcriptService = transcriptService;
        _contextManager = contextManager;
        _logger = logger;
    }

    /// <summary>错误行为策略：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>流式旁路落盘节流：累积字符阈值（ADR 0166 — 与 200ms 时间阈值先到者触发）。</summary>
    private const int StreamingFlushChars = 512;

    /// <summary>流式旁路落盘节流：时间阈值（Stopwatch tick，200ms）。</summary>
    private static readonly long StreamingFlushTicks = Stopwatch.Frequency / 5;

    /// <summary>
    /// 透传下游事件 → 下游完成后取快照差量写 transcript。
    /// 不缓冲事件流，保证流式响应实时性（对齐 SaveContextMiddleware 模式）。
    /// ADR 0166：透传途中按节流把「已生成的累积全文」写进旁路，打断/崩溃后由读端收编。
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct) {
        // Bug 19 根因 A 修复：startCount 必须基于含系统提示词的快照计数，与 GetMessageListAsync 索引空间一致
        var startSnapshot = await _contextManager.GetMessageListAsync(ct).ConfigureAwait(false);
        var startCount = startSnapshot.Count;

        // ADR 0166 — 流式旁路：只在能落盘的场合累积（dry run / worker / 无服务一律不写）
        var streaming = ShouldPersist(context) ? new StringBuilder() : null;
        var lastFlushTicks = 0L;

        await foreach (var evt in next(context, ct).ConfigureAwait(false)) {
            if (streaming is not null && IsAssistantTextDelta(evt)) {
                streaming.Append(evt.Content);
                var now = Stopwatch.GetTimestamp();
                if (streaming.Length >= StreamingFlushChars || now - lastFlushTicks >= StreamingFlushTicks) {
                    lastFlushTicks = now;
                    await AppendStreamingSnapshotSafeAsync(streaming.ToString(), ct).ConfigureAwait(false);
                }
            }

            yield return evt;
        }

        if (context.IsDryRun || _transcriptService is null)
            yield break;

        // Worker 进程不写主 session 文件 — 对齐原 CliSession.AppendTranscriptEntriesAsync 守卫
        var agentRole = Environment.GetEnvironmentVariable(JccEnvVar.AgentRole.ToValue());
        if (string.Equals(agentRole, "worker", StringComparison.OrdinalIgnoreCase))
            yield break;

        var snapshot = await _contextManager.GetMessageListAsync(ct).ConfigureAwait(false);
        if (snapshot.Count <= startCount) {
            // 正常结束但本轮没有新增消息（空回合）— 旁路也要清，否则残留会被下次加载收编成幽灵消息
            await ClearStreamingSafeAsync(ct).ConfigureAwait(false);
            yield break;
        }

        var entries = new List<TranscriptEntry>(snapshot.Count - startCount);
        for (var i = startCount; i < snapshot.Count; i++) {
            var message = snapshot[i];
            // Bug 19 根因 B 修复：空 content 的 assistant 消息（工具调用轮次）仍写入 transcript，
            // 用占位文本避免恢复时丢失 assistant 轮次（否则 messages 数组只有 user 没有 assistant）
            var content = message.Content;
            if (string.IsNullOrEmpty(content) && string.Equals(message.Role.ToValue(), "assistant", StringComparison.OrdinalIgnoreCase))
                content = "(tool call)";
            if (string.IsNullOrEmpty(content))
                continue;
            entries.Add(new TranscriptEntry {
                Role = message.Role.ToValue(),
                Content = content,
                Timestamp = DateTime.UtcNow,
                ModelId = context.FinalModelId,
            });
        }

        if (entries.Count == 0) {
            await ClearStreamingSafeAsync(ct).ConfigureAwait(false);
            yield break;
        }

        try {
            await _transcriptService.AppendEntriesAsync(_contextManager.SessionId, entries, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            _logger?.LogError(ex, "[TranscriptPersist] transcript 写入失败（会话 {SessionId}），本轮对话可能丢失", _contextManager.SessionId);
        }

        // ADR 0166 — 本回合已完整落盘，旁路使命结束
        await ClearStreamingSafeAsync(ct).ConfigureAwait(false);
    }

    /// <summary>是否应在本回合写流式旁路（dry run / worker 进程 / 无 transcript 服务都不写）。</summary>
    /// <param name="context">中间件上下文。</param>
    /// <returns>是否写旁路。</returns>
    private bool ShouldPersist(ChatMiddlewareContext context) {
        if (context.IsDryRun || _transcriptService is null) return false;
        var agentRole = Environment.GetEnvironmentVariable(JccEnvVar.AgentRole.ToValue());
        return !string.Equals(agentRole, "worker", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>是否为助手正文增量（只累积 Content；思考/工具事件不属回复正文）。</summary>
    /// <param name="evt">流事件。</param>
    /// <returns>是否正文增量。</returns>
    private static bool IsAssistantTextDelta(ChatStreamEvent evt) =>
        evt.Type == ChatStreamEventType.Content && !string.IsNullOrEmpty(evt.Content);

    /// <summary>写旁路快照 — 失败只记日志，绝不打断对话（与 OnError=Continue 语义一致）。</summary>
    /// <param name="content">累积全文。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task AppendStreamingSnapshotSafeAsync(string content, CancellationToken ct) {
        if (_transcriptService is null) return;
        try {
            await _transcriptService.AppendStreamingSnapshotAsync(_contextManager.SessionId, content, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[STREAM-PERSIST] 旁路写入失败（会话 {SessionId}），本回合退回仅在结束时落盘", _contextManager.SessionId);
        }
    }

    /// <summary>清理旁路 — 失败只记日志（残留旁路最多多一条残稿，不会污染主记录语义）。</summary>
    /// <param name="ct">取消令牌。</param>
    private async Task ClearStreamingSafeAsync(CancellationToken ct) {
        if (_transcriptService is null) return;
        try {
            await _transcriptService.ClearStreamingAsync(_contextManager.SessionId, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[STREAM-PERSIST] 旁路清理失败（会话 {SessionId}）", _contextManager.SessionId);
        }
    }
}