
namespace Core.Bridge.Init.V1;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// V1 写入崩溃恢复指针 — 对齐 TS 端: writeBridgePointer
/// best-effort: 写入失败不阻塞主流程
/// </summary>
[Register]
internal sealed class V1PointerWriteMiddleware : IMiddleware<V1BridgeInitContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(V1BridgeInitContext ctx, MiddlewareDelegate<V1BridgeInitContext> next, CancellationToken ct)
    {
        var pointerService = new BridgePointerService(ctx.FileSystem, ctx.Logger);
        await pointerService.WriteAsync(ctx.Parameters.Dir, new BridgePointer
        {
            SessionId = ctx.SessionId!,
            EnvironmentId = ctx.EnvironmentId!,
            Source = BridgePointerSource.Repl.ToValue(),
        }, ct).ConfigureAwait(false);

        ctx.Parameters.OnStateChange?.Invoke(BridgeState.Ready, null);
        await next(ctx, ct).ConfigureAwait(false);
    }
}
