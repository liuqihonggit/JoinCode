namespace Core.Scheduling.Tasks;


[Register(typeof(ITeammateExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class TeammateContinuousModeMiddleware : ServiceEntity, ITeammateExecutionMiddleware
{

    public TeammateContinuousModeMiddleware(IClockService clock, ILogger<TeammateContinuousModeMiddleware>? logger = null)
    {
        _clock = clock;
        _logger = logger;
    }
    private readonly ILogger<TeammateContinuousModeMiddleware>? _logger;
    private readonly IClockService _clock;


    public Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct)
    {
        if (!ctx.Definition.ContinuousMode)
        {
            return next(ctx, ct);
        }

        if (ctx.RunLoopAsync is not null && ctx.LifecycleCts is not null)
        {
            _ = ctx.RunLoopAsync(ctx.Definition, ctx.State ?? throw new InvalidOperationException("Teammate state is not available."), ctx.LifecycleCts.Token);
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
