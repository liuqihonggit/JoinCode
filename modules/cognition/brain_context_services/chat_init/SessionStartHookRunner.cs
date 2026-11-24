namespace Core.Context;

/// <summary>
/// 会话启动 Hook 的唯一执行入口（TASK130-A6）。
/// <para>
/// 两个调用点：① <see cref="SessionStartHookMiddleware"/>（<c>chatinit-pipeline</c>，仅无主代理的
/// ChatService 回落链会跑）；② <c>SessionController</c> 主代理链首回合（会话初始化管道在主链从未执行，
/// 见 TASK130 A6 的 P0 记录）。逻辑只此一份，避免两条链各写一遍再漂移。
/// </para>
/// </summary>
public static class SessionStartHookRunner {
    /// <summary>执行 SessionStart hook，并把 <c>additionalContext</c> 落到会话级暂存。</summary>
    /// <param name="manager">Hook 编排入口；null 表示本文宿主无 hook 子系统，直接返回。</param>
    /// <param name="store">上下文暂存；null 表示不注入（调用方仍可拿到返回值）。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="source">启动来源（hook matcher 用）。</param>
    /// <param name="logger">可选日志记录器。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<SessionStartHookResult?> RunAsync(
        ISessionStartHookManager? manager,
        ISessionStartContextStore? store,
        string sessionId,
        string source,
        ILogger? logger = null,
        CancellationToken ct = default) {
        if (manager is null) return null;

        var result = await manager.OnSessionStartAsync(
            new SessionStartHookContext { SessionId = sessionId, Source = source }, ct).ConfigureAwait(false);

        if (!result.ShouldProceed) {
            logger?.LogWarning("[SessionStart] 会话启动被 Hook 阻止: {Message}", result.Message);
        }

        // 无内容也写一次 null：同一进程内换会话 / 重跑初始化时，旧上下文不许残留。
        store?.Set(sessionId, result.AdditionalContext);

        if (result.AdditionalContext is { Length: > 0 } additionalContext) {
            Diag.WriteLine($"[SESSION-CONTEXT] session={sessionId} 收到 additionalContext {additionalContext.Length} 字符");
        }

        // initialUserMessage 只透传不消费：本仓无「无输入自动开轮」模式，自动插一条 user 消息
        // 会凭空产生一个用户回合 + 一次模型调用（见 TASK130 A6 判定）。
        if (result.InitialUserMessage is { Length: > 0 } initialUserMessage) {
            logger?.LogDebug(
                "[SessionStart] 忽略 initialUserMessage（{Length} 字符）：本仓无无输入自动开轮模式",
                initialUserMessage.Length);
        }

        return result;
    }
}
