namespace Core.Scheduling.Tasks;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(ITeammateExecutionMiddleware))]
public sealed partial class TeammatePlanModeMiddleware : ITeammateExecutionMiddleware
{
    [Inject] private readonly IPlanModeManager? _planModeManager;
    [Inject] private readonly ILogger<TeammatePlanModeMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct)
    {
        if (ctx.Definition.PlanModeRequired && _planModeManager != null && !_planModeManager.IsInPlanMode)
        {
            try
            {
                _logger?.LogInformation("Teammate {TeammateId} requires plan mode, entering automatically", ctx.Definition.TeammateId);

                var planResult = await _planModeManager.EnterPlanModeAsync(
                    description: $"Teammate {ctx.Definition.TeammateId}: {ctx.Definition.Task}",
                    cancellationToken: ct).ConfigureAwait(false);

                if (!planResult.Success)
                {
                    _logger?.LogWarning("Teammate {TeammateId} failed to enter plan mode: {Error}", ctx.Definition.TeammateId, planResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Teammate {TeammateId} failed to enter plan mode", ctx.Definition.TeammateId);
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
