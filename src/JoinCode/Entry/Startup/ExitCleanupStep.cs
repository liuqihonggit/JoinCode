namespace JoinCode.Entry;

/// <summary>
/// 退出清理中间件 — 打印成本摘要、触发停止 Hook
/// </summary>
[Register]
internal sealed partial class ExitCleanupStep : IMiddleware<StartupContext>
{
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        var host = context.Host;

        var costSummaryHook = host.Services.GetService<Core.CostTracking.ICostSummaryHook>();
        if (costSummaryHook is not null)
        {
            await costSummaryHook.PrintSummaryOnExitAsync(ct).ConfigureAwait(false);
        }

        var stopHookManager = host.Services.GetService<IStopHookManager>();
        if (stopHookManager is not null)
        {
            var stopContext = new StopHookContext { SessionId = "main", Reason = "application-exit" };
            await stopHookManager.OnStopAsync(stopContext, ct).ConfigureAwait(false);
        }

        Cli.TerminalHelper.WriteLine("正在退出应用程序...再见！");

        await next(context, ct);
    }
}
