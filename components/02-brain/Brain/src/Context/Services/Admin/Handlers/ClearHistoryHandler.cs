using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 清空历史操作处理器 — 对齐 TS: clear 前 SessionEnd Hook, clear 后 SessionStart Hook
/// </summary>
[Register]
public sealed partial class ClearHistoryHandler : IChatAdminOperationHandler
{
    private readonly IChatPromptManager _promptManager;
    private readonly ISessionStats _sessionStats;
    private readonly IChatIdleDetector _idleDetector;
    private readonly IChatInitializer _initializer;
    private readonly SessionHookHelper _hookHelper;

    public ClearHistoryHandler(
        IChatPromptManager promptManager,
        ISessionStats sessionStats,
        IChatIdleDetector idleDetector,
        IChatInitializer initializer,
        SessionHookHelper hookHelper)
    {
        _promptManager = promptManager;
        _sessionStats = sessionStats;
        _idleDetector = idleDetector;
        _initializer = initializer;
        _hookHelper = hookHelper;
    }

    public ChatAdminOperation Operation => ChatAdminOperation.ClearHistory;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        try
        {
            var sessionId = (context.ContextManager is ChatContextManager cm) ? cm.SessionId : "default";

            await _hookHelper.ExecuteSessionEndHookAsync(sessionId, "clear", ct).ConfigureAwait(false);

            await _initializer.SaveCurrentCostsAsync(sessionId, ct).ConfigureAwait(false);

            var staticPrefix = await _promptManager.GetStaticPrefixAsync().ConfigureAwait(false);

            await context.ContextManager.ClearMessagesAsync(ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(staticPrefix))
            {
                await context.ContextManager.UpdateSystemPromptAsync(staticPrefix, ct).ConfigureAwait(false);
            }

            _promptManager.ClearCache();
            await _promptManager.ClearRemindersAsync(ct).ConfigureAwait(false);

            _sessionStats.Reset();
            _idleDetector.Reset();

            await _hookHelper.ExecuteSessionStartHookAsync(sessionId, "clear", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Error = ex;
        }
    }
}
