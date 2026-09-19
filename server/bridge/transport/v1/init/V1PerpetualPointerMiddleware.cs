namespace Core.Bridge.Init.V1;


/// <summary>
/// V1 Perpetual 模式: 读取崩溃恢复指针 — 对齐 TS 端: readBridgePointer
/// best-effort: 读取失败不阻塞主流程
/// </summary>
[Register(typeof(IMiddleware<V1BridgeInitContext>), ServiceLifetime.Singleton)]
internal sealed partial class V1PerpetualPointerMiddleware : ServiceEntity, IMiddleware<V1BridgeInitContext> {
    /// <summary>错误行为 — 继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行中间件 — Perpetual 模式下读取崩溃恢复指针，best-effort 不阻塞主流程
    /// </summary>
    /// <param name="ctx">V1 Bridge 初始化上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(V1BridgeInitContext ctx, MiddlewareDelegate<V1BridgeInitContext> next, CancellationToken ct) {
        if (ctx.Parameters.Perpetual) {
            var pointerService = new BridgePointerService(ctx.FileSystem, ctx.Logger);
            var rawPrior = await pointerService.ReadAsync(ctx.Parameters.Dir, ct).ConfigureAwait(false);
            if (rawPrior?.Pointer.Source == BridgePointerSource.Repl.ToValue()) {
                ctx.PriorPointer = rawPrior.Pointer;
                ctx.Logger?.LogInformation("Bridge v1: Perpetual 模式发现已有指针: env={EnvId} session={SessionId}",
                    rawPrior.Pointer.EnvironmentId, rawPrior.Pointer.SessionId);
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}