namespace Core.Context;

/// <summary>
/// 会话启动 Hook 的 <c>additionalContext</c> 注入 — TASK130（A6）。
/// <para>
/// SessionStart hook 产出的上下文在会话初始化时落到 <see cref="ISessionStartContextStore"/>，
/// 本中间件每回合把它补写成一条动态系统消息。必须每回合重写的原因：
/// <c>SystemPromptMiddleware</c>（priority 100）在本节点之前刚刚 <c>ClearDynamicSystemMessagesAsync</c>，
/// 只写一次会在首轮就被清掉。
/// </para>
/// <para><b>为什么不进 transcript</b>：动态系统消息独立于会话日志，只随前缀组装（同
/// <c>MemoryRecallMiddleware</c>）；文本每回合恒定，前缀缓存仍命中。</para>
/// </summary>
[Register(typeof(IPreparePreprocessMiddleware), ServiceLifetime.Singleton)]
public sealed partial class SessionStartContextMiddleware : ServiceEntity, IPreparePreprocessMiddleware {
    private readonly IChatContextManager? _contextManager;
    private readonly ISessionStartContextStore? _store;

    /// <summary>构造会话启动上下文注入中间件。</summary>
    /// <param name="contextManager">聊天上下文管理器（写动态系统消息的入口）。</param>
    /// <param name="store">会话启动上下文暂存（无 hook 构造时为 null → 恒不注入）。</param>
    public SessionStartContextMiddleware(
        IChatContextManager? contextManager = null,
        ISessionStartContextStore? store = null) {
        _contextManager = contextManager;
        _store = store;
    }

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct) {
        // fail-soft：注入是锦上添花，任何异常都不许拦住回合（与 MemoryRecallMiddleware 同纪律）。
        try {
            if (_contextManager is not null && _store is not null) {
                var text = _store.Get(_contextManager.SessionId);
                if (!string.IsNullOrWhiteSpace(text)) {
                    await _contextManager.AddDynamicSystemMessageAsync(text, ct).ConfigureAwait(false);
                    Diag.WriteLine($"[SESSION-CONTEXT] 注入 {text.Length} 字符 session={_contextManager.SessionId}");
                }
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            Diag.WriteLine($"[SESSION-CONTEXT] 注入跳过: {ex.GetType().Name}: {ex.Message}");
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
