namespace Core.Scheduling.Tasks;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(ITeammateExecutionMiddleware))]
public sealed partial class TeammateValidationMiddleware : ITeammateExecutionMiddleware
{
    [Inject] private readonly ILogger<TeammateValidationMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx.Definition);

        _logger?.LogInformation(L.T(StringKey.InProcessTeammateStartLog),
            ctx.Definition.TeammateId, ctx.Definition.Task, ctx.Definition.ContinuousMode);

        return next(ctx, ct);
    }
}
