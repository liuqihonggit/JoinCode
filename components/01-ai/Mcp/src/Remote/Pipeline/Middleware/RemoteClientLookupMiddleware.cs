namespace McpToolRegistry;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 客户端查找中间件 — 从远程客户端字典中获取客户端和旧规格
/// </summary>
[Register(typeof(IRemoteSyncMiddleware))]
public sealed partial class RemoteClientLookupMiddleware : IRemoteSyncMiddleware
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

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
