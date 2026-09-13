namespace Core.Context;

/// <summary>
/// 会话管理中间件 — 操作分派层，通过 IChatAdminOperationHandler 策略模式分发
/// 每个 Handler 只注入自己需要的服务，替代原来的 11 路 switch + 8 个服务注入
/// </summary>
[Register(typeof(IChatAdminMiddleware), ServiceLifetime.Singleton)]
public sealed partial class SessionAdminMiddleware : ServiceEntity, IChatAdminMiddleware
{
    private readonly Dictionary<ChatAdminOperation, IChatAdminOperationHandler> _handlers;

    /// <summary>
    /// 初始化 <see cref="SessionAdminMiddleware"/> 实例，按操作类型构建处理器分派表
    /// </summary>
    /// <param name="handlers">所有已注册的管理操作处理器集合</param>
    public SessionAdminMiddleware(IEnumerable<IChatAdminOperationHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Operation);
    }

    /// <summary>
    /// 根据 Operation 分派到对应的 Handler — 策略模式替代 switch
    /// </summary>
    public async Task InvokeAsync(ChatAdminContext context, MiddlewareDelegate<ChatAdminContext> next, CancellationToken ct)
    {
        if (_handlers.TryGetValue(context.Operation, out var handler))
        {
            await handler.ExecuteAsync(context, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
