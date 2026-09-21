namespace JoinCode.Entry;

/// <summary>
/// 退出清理中间件 — 打印成本摘要、触发停止 Hook
/// </summary>
[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
internal sealed partial class ExitCleanupStep : ServiceEntity, IMiddleware<StartupContext> {
    /// <summary>
    /// 中间件入口 — 打印成本摘要、触发停止 Hook 后调用下一环节
    /// </summary>
    /// <param name="context">启动上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct) {
        var host = context.Host;

        var costSummaryHook = host.Services.GetService<Core.CostTracking.ICostSummaryHook>();
        if (costSummaryHook is not null) {
            await costSummaryHook.PrintSummaryOnExitAsync(ct).ConfigureAwait(false);
        }

        var stopHookManager = host.Services.GetService<IStopHookManager>();
        if (stopHookManager is not null) {
            var stopContext = new StopHookContext { SessionId = global::Core.Utils.SessionIdFactory.DefaultSessionId, Reason = "application-exit" };
            await stopHookManager.OnStopAsync(stopContext, ct).ConfigureAwait(false);
        }

        Cli.TerminalHelper.WriteLine("正在退出应用程序...再见！");

        await next(context, ct).ConfigureAwait(false);
    }
}