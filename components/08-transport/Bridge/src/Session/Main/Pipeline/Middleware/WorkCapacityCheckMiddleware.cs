namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IHandleWorkMiddleware))]
public sealed partial class WorkCapacityCheckMiddleware : IHandleWorkMiddleware
{
    [Inject] private readonly ILogger<WorkCapacityCheckMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct)
    {
        _logger?.LogInformation("BridgeMain: received work, WorkId={WorkId}, SessionId={SessionId}, WorkType={WorkType}",
            ctx.Work.WorkId, ctx.Work.SessionId, ctx.Work.WorkType);

        if (ctx.ActiveSessions.Count >= ctx.Config.MaxSessions)
        {
            _logger?.LogWarning("BridgeMain: at capacity, skipping work {WorkId}", ctx.Work.WorkId);
            ctx.ShortCircuited = true;
            return;
        }

        if (ctx.CompletedWorkIds.Contains(ctx.Work.WorkId))
        {
            _logger?.LogDebug("BridgeMain: skipping duplicate work {WorkId}", ctx.Work.WorkId);

            if (ctx.ActiveSessions.Count >= ctx.Config.MaxSessions)
            {
                var pollConfig = ctx.PollConfig;
                var delayMs = pollConfig?.NonExclusiveHeartbeatIntervalMs > 0
                    ? pollConfig.NonExclusiveHeartbeatIntervalMs
                    : pollConfig?.HeartbeatIntervalMs ?? 30000;
                try
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }

            ctx.ShortCircuited = true;
            return;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
