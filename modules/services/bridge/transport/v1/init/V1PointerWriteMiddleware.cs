namespace Core.Bridge.Init.V1;


/// <summary>
/// V1 写入崩溃恢复指针 — 对齐 TS 端: writeBridgePointer
/// best-effort: 写入失败不阻塞主流程
/// </summary>
[Register(typeof(IMiddleware<V1BridgeInitContext>), ServiceLifetime.Singleton)]
internal sealed partial class V1PointerWriteMiddleware : ServiceEntity, IMiddleware<V1BridgeInitContext> {
    /// <summary>错误行为 — 继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行中间件 — 写入崩溃恢复指针并触发 Ready 状态变更
    /// </summary>
    /// <param name="ctx">V1 桥初始化上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(V1BridgeInitContext ctx, MiddlewareDelegate<V1BridgeInitContext> next, CancellationToken ct) {
        var pointerService = new BridgePointerService(ctx.FileSystem, ctx.Logger);
        await pointerService.WriteAsync(ctx.Parameters.Dir, new BridgePointer {
            SessionId = ctx.SessionId ?? throw new InvalidOperationException("SessionId not set."),
            EnvironmentId = ctx.EnvironmentId ?? throw new InvalidOperationException("EnvironmentId not set."),
            Source = BridgePointerSource.Repl.ToValue(),
        }, ct).ConfigureAwait(false);

        ctx.Parameters.OnStateChange?.Invoke(BridgeState.Ready, null);
        await next(ctx, ct).ConfigureAwait(false);
    }
}