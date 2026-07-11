using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 会话 Hook 辅助服务 — ClearHistory/CompactHistory 共用的 SessionStart/SessionEnd Hook 执行逻辑
/// </summary>
[Register]
public sealed partial class SessionHookHelper
{
    [Inject] private readonly ISessionStartHookManager? _sessionStartHookManager;
    [Inject] private readonly IHookOrchestrator? _hookOrchestrator;
    [Inject] private readonly ILogger<SessionHookHelper>? _logger;

    /// <summary>
    /// 执行 SessionStart Hook — 对齐 TS processSessionStartHooks
    /// </summary>
    public async Task ExecuteSessionStartHookAsync(string sessionId, string source, CancellationToken ct)
    {
        if (_sessionStartHookManager is null) return;

        try
        {
            var hookContext = new SessionStartHookContext
            {
                SessionId = sessionId,
                Source = source
            };
            await _sessionStartHookManager.OnSessionStartAsync(hookContext, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SessionStart Hook 执行失败 (source={Source})", source);
        }
    }

    /// <summary>
    /// 执行 SessionEnd Hook — 对齐 TS executeSessionEndHooks
    /// </summary>
    public async Task ExecuteSessionEndHookAsync(string sessionId, string reason, CancellationToken ct)
    {
        if (_hookOrchestrator is null) return;

        try
        {
            var payload = new Dictionary<string, JsonElement>
            {
                ["sessionId"] = JsonElementHelper.FromString(sessionId),
                ["reason"] = JsonElementHelper.FromString(reason)
            };

            await foreach (var _ in _hookOrchestrator.ExecuteHooksAsync(
                HookEvent.SessionEnd,
                payload,
                matcher: reason,
                sessionId: sessionId,
                cancellationToken: ct).ConfigureAwait(false))
            {
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SessionEnd Hook 执行失败 (reason={Reason})", reason);
        }
    }
}
