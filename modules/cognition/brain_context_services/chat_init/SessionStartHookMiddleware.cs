namespace Core.Context;

/// <summary>
/// 会话启动 Hook 中间件 — 执行会话启动 Hook，允许外部逻辑阻止会话启动，
/// 并把 hook 产出的 <c>additionalContext</c> 落到会话级暂存（TASK130-A6）。
/// </summary>
/// <remarks>
/// 本中间件在 <c>chatinit-pipeline</c> 里只跑一次（priority 400，已在 <c>context-load-mw</c> 之后，
/// 故 <see cref="ChatInitContext.SessionId"/> 是真实会话）；真正写进提示词的是
/// <see cref="SessionStartContextMiddleware"/>（每回合重写，避免被 system-prompt-mw 清掉）。
/// 执行逻辑委托 <see cref="SessionStartHookRunner"/> —— 主代理链另有调用点，不能各写一份。
/// </remarks>
[Register(typeof(IChatInitMiddleware), ServiceLifetime.Singleton)]
public sealed partial class SessionStartHookMiddleware : ServiceEntity, IChatInitMiddleware {

    /// <summary>
    /// 初始化 <see cref="SessionStartHookMiddleware"/> 实例
    /// </summary>
    /// <param name="sessionStartHookManager">可选的会话启动 Hook 管理器</param>
    /// <param name="sessionStartContextStore">可选的会话启动上下文暂存</param>
    /// <param name="logger">可选的日志记录器</param>
    public SessionStartHookMiddleware(
        ISessionStartHookManager? sessionStartHookManager = null,
        ISessionStartContextStore? sessionStartContextStore = null,
        ILogger<SessionStartHookMiddleware>? logger = null) {
        _sessionStartHookManager = sessionStartHookManager;
        _sessionStartContextStore = sessionStartContextStore;
        _logger = logger;
    }
    private readonly ISessionStartHookManager? _sessionStartHookManager;
    private readonly ISessionStartContextStore? _sessionStartContextStore;
    private readonly ILogger<SessionStartHookMiddleware>? _logger;

    /// <summary>会话 Hook 在配置监控之后</summary>

    /// <summary>会话 Hook 失败不应中断管道</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行会话启动 Hook
    /// </summary>
    public async Task InvokeAsync(ChatInitContext context, MiddlewareDelegate<ChatInitContext> next, CancellationToken ct) {
        await SessionStartHookRunner.RunAsync(
            _sessionStartHookManager, _sessionStartContextStore, context.SessionId, "tui", _logger, ct).ConfigureAwait(false);

        await next(context, ct).ConfigureAwait(false);
    }
}
