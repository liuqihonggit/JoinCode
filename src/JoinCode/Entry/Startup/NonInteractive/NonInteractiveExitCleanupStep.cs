namespace JoinCode.Entry;

[Register]
internal sealed partial class NonInteractiveExitCleanupStep : IMiddleware<StartupContext>
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
            var stopContext = new StopHookContext { SessionId = "main", Reason = "non-interactive-exit" };
            await stopHookManager.OnStopAsync(stopContext, ct).ConfigureAwait(false);
        }

        await next(context, ct);
    }
}
