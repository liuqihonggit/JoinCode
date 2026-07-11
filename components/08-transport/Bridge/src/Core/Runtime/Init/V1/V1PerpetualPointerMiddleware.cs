
namespace Core.Bridge.Init.V1;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// V1 Perpetual 模式: 读取崩溃恢复指针 — 对齐 TS 端: readBridgePointer
/// best-effort: 读取失败不阻塞主流程
/// </summary>
[Register]
internal sealed class V1PerpetualPointerMiddleware : IMiddleware<V1BridgeInitContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(V1BridgeInitContext ctx, MiddlewareDelegate<V1BridgeInitContext> next, CancellationToken ct)
    {
        if (ctx.Parameters.Perpetual)
        {
            var pointerService = new BridgePointerService(ctx.FileSystem, ctx.Logger);
            var rawPrior = await pointerService.ReadAsync(ctx.Parameters.Dir, ct).ConfigureAwait(false);
            if (rawPrior?.Pointer.Source == BridgePointerSource.Repl.ToValue())
            {
                ctx.PriorPointer = rawPrior.Pointer;
                ctx.Logger?.LogInformation("Bridge v1: Perpetual 模式发现已有指针: env={EnvId} session={SessionId}",
                    rawPrior.Pointer.EnvironmentId, rawPrior.Pointer.SessionId);
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
