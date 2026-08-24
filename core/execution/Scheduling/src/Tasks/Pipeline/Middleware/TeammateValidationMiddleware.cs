namespace Core.Scheduling.Tasks;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(ITeammateExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class TeammateValidationMiddleware : ServiceEntity, ITeammateExecutionMiddleware
{

    public TeammateValidationMiddleware(ILogger<TeammateValidationMiddleware>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<TeammateValidationMiddleware>? _logger;


    public Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx.Definition);

        _logger?.LogInformation(L.T(StringKey.InProcessTeammateStartLog),
            ctx.Definition.TeammateId, ctx.Definition.Task, ctx.Definition.ContinuousMode);

        return next(ctx, ct);
    }
}
