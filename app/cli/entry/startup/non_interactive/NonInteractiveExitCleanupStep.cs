namespace JoinCode.Entry;

[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
internal sealed partial class NonInteractiveExitCleanupStep : ServiceEntity, IMiddleware<StartupContext> {
    /// <summary>执行非交互模式退出清理中间件 — 打印成本摘要并触发停止钩子后传递给下一个中间件</summary>
    /// <param name="context">启动上下文，包含宿主与服务容器</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct) {
        var host = context.Host;

        var costSummaryHook = host.Services.GetService<Core.CostTracking.ICostSummaryHook>();
        if (costSummaryHook is not null) {
            await costSummaryHook.PrintSummaryOnExitAsync(ct).ConfigureAwait(false);
        }

        var stopHookManager = host.Services.GetService<IStopHookManager>();
        if (stopHookManager is not null) {
            var stopContext = new StopHookContext { SessionId = global::Core.Utils.SessionIdFactory.DefaultSessionId, Reason = "non-interactive-exit" };
            await stopHookManager.OnStopAsync(stopContext, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}