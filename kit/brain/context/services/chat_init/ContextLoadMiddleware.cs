namespace Core.Context;

/// <summary>
/// 上下文加载中间件 — 加载聊天上下文并初始化内容替换状态
/// 对齐 TS REPL.tsx: provisionContentReplacementState(initialMessages, initialContentReplacements)
/// </summary>
[Register(typeof(IChatInitMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ContextLoadMiddleware : ServiceEntity, IChatInitMiddleware
{

    public ContextLoadMiddleware(IChatContentReplacer contentReplacer, ILogger<ContextLoadMiddleware>? logger = null)
    {
        _contentReplacer = contentReplacer;
        _logger = logger;
    }
    private readonly IChatContentReplacer _contentReplacer;
    private readonly ILogger<ContextLoadMiddleware>? _logger;

    /// <summary>上下文加载最先执行</summary>

    /// <summary>上下文加载失败应中断管道</summary>

    /// <summary>
    /// 加载聊天上下文并初始化内容替换状态
    /// </summary>
    public async Task InvokeAsync(ChatInitContext context, MiddlewareDelegate<ChatInitContext> next, CancellationToken ct)
    {
        await context.ContextManager.LoadContextAsync(ct).ConfigureAwait(false);

        // 设置 SessionId — 供后续中间件使用
        context.SessionId = (context.ContextManager is ChatContextManager cm) ? cm.SessionId : global::Core.Utils.SessionIdFactory.DefaultSessionId;

        // 初始化内容替换状态 — 功能开关关闭时返回 null，query 会跳过整个预算执行
        // 有历史消息时走重建路径，保证恢复会话时 prompt cache 一致性
        if (context.ToolUseContext.ContentReplacementState is null)
        {
            var initialMessages = await context.ContextManager.GetMessageListAsync(ct).ConfigureAwait(false);
            context.ToolUseContext.ContentReplacementState = _contentReplacer.ProvisionState(initialMessages.ToList());
        }

        _logger?.LogDebug("[ChatInit] 上下文加载完成，SessionId: {SessionId}", context.SessionId);

        await next(context, ct).ConfigureAwait(false);
    }
}
