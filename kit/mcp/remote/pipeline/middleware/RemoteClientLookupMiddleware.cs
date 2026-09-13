namespace McpToolRegistry;


/// <summary>
/// 客户端查找中间件 — 从远程客户端字典中获取客户端和旧规格
/// </summary>
[Register(typeof(IRemoteSyncMiddleware), ServiceLifetime.Singleton)]
public sealed partial class RemoteClientLookupMiddleware : ServiceEntity, IRemoteSyncMiddleware
{

    /// <summary>
    /// 执行中间件逻辑 — 校验远程客户端是否存在，不存在则标记失败
    /// </summary>
    /// <param name="ctx">远程同步上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>异步任务</returns>
    public Task InvokeAsync(RemoteSyncContext ctx, MiddlewareDelegate<RemoteSyncContext> next, CancellationToken ct)
    {
        if (ctx.Client is null)
        {
            ctx.Success = false;
            ctx.ErrorMessage = $"客户端 '{ctx.ClientId}' 未找到";
            return Task.CompletedTask;
        }

        return next(ctx, ct);
    }
}
