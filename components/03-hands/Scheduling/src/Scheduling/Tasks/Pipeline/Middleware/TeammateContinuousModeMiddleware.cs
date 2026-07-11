namespace Core.Scheduling.Tasks;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(ITeammateExecutionMiddleware))]
public sealed partial class TeammateContinuousModeMiddleware : ITeammateExecutionMiddleware
{
    [Inject] private readonly ILogger<TeammateContinuousModeMiddleware>? _logger;
    [Inject] private readonly IClockService _clock;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct)
    {
        if (!ctx.Definition.ContinuousMode)
        {
            return next(ctx, ct);
        }

        if (ctx.RunLoopAsync is not null && ctx.LifecycleCts is not null)
        {
            _ = ctx.RunLoopAsync(ctx.Definition, ctx.State!, ctx.LifecycleCts.Token);
        }

        var elapsed = (long)(_clock.GetUtcNow() - ctx.StartTime).TotalMilliseconds;
        ctx.Result = AgentTaskResult.Success(
            ctx.Definition.TaskId,
            ctx.Definition.TeammateId,
            "Teammate started in continuous mode",
            elapsed);
        ctx.ContinuousModeHandled = true;

        return Task.CompletedTask;
    }
}
