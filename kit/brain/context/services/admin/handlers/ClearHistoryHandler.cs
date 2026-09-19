namespace Core.Context;

/// <summary>
/// 清空历史操作处理器 — 对齐 TS: clear 前 SessionEnd Hook, clear 后 SessionStart Hook
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class ClearHistoryHandler : ServiceEntity, IChatAdminOperationHandler {
    private readonly IChatPromptManager _promptManager;
    private readonly ISessionStats _sessionStats;
    private readonly IChatIdleDetector _idleDetector;
    private readonly IChatInitializer _initializer;
    private readonly SessionHookHelper _hookHelper;

    /// <summary>
    /// 初始化 <see cref="ClearHistoryHandler"/> 实例
    /// </summary>
    /// <param name="promptManager">聊天提示词管理器，用于获取静态前缀和清理提醒</param>
    /// <param name="sessionStats">会话统计服务，清空后重置统计</param>
    /// <param name="idleDetector">空闲检测器，清空后重置检测状态</param>
    /// <param name="initializer">聊天初始化器，用于保存当前成本</param>
    /// <param name="hookHelper">会话 Hook 辅助服务，用于执行 SessionEnd/SessionStart Hook</param>
    public ClearHistoryHandler(
        IChatPromptManager promptManager,
        ISessionStats sessionStats,
        IChatIdleDetector idleDetector,
        IChatInitializer initializer,
        SessionHookHelper hookHelper) {
        _promptManager = promptManager;
        _sessionStats = sessionStats;
        _idleDetector = idleDetector;
        _initializer = initializer;
        _hookHelper = hookHelper;
    }

    /// <summary>
    /// 获取该处理器负责的管理操作类型
    /// </summary>
    public ChatAdminOperation Operation => ChatAdminOperation.ClearHistory;

    /// <summary>
    /// 执行清空历史操作：触发 SessionEnd Hook、保存成本、清空消息、恢复静态前缀、重置统计与空闲检测，再触发 SessionStart Hook
    /// </summary>
    /// <param name="context">管理操作上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct) {
        try {
            var sessionId = (context.ContextManager is ChatContextManager cm) ? cm.SessionId : global::Core.Utils.SessionIdFactory.DefaultSessionId;

            await _hookHelper.ExecuteSessionEndHookAsync(sessionId, "clear", ct).ConfigureAwait(false);

            await _initializer.SaveCurrentCostsAsync(sessionId, ct).ConfigureAwait(false);

            var staticPrefix = await _promptManager.GetStaticPrefixAsync().ConfigureAwait(false);

            await context.ContextManager.ClearMessagesAsync(ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(staticPrefix)) {
                await context.ContextManager.UpdateSystemPromptAsync(staticPrefix, ct).ConfigureAwait(false);
            }

            _promptManager.ClearCache();
            await _promptManager.ClearRemindersAsync(ct).ConfigureAwait(false);

            _sessionStats.Reset();
            _idleDetector.Reset();

            await _hookHelper.ExecuteSessionStartHookAsync(sessionId, "clear", ct).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Error = ex;
        }
    }
}