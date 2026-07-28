using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 会话启动 Hook 中间件 — 执行会话启动 Hook，允许外部逻辑阻止会话启动
/// </summary>
[Register(typeof(IChatInitMiddleware))]
public sealed partial class SessionStartHookMiddleware : IChatInitMiddleware
{
    [Inject] private readonly ISessionStartHookManager? _sessionStartHookManager;
    [Inject] private readonly ILogger<SessionStartHookMiddleware>? _logger;

    /// <summary>会话 Hook 在配置监控之后</summary>

    /// <summary>会话 Hook 失败不应中断管道</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行会话启动 Hook
    /// </summary>
    public async Task InvokeAsync(ChatInitContext context, MiddlewareDelegate<ChatInitContext> next, CancellationToken ct)
    {
        if (_sessionStartHookManager is not null)
        {
            var sessionId = context.SessionId;
            var startContext = new SessionStartHookContext
            {
                SessionId = sessionId,
                Source = "tui"
            };
            var startResult = await _sessionStartHookManager.OnSessionStartAsync(startContext).ConfigureAwait(false);
            if (!startResult.ShouldProceed)
            {
                _logger?.LogWarning("[SessionStart] 会话启动被 Hook 阻止: {Message}", startResult.Message);
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
