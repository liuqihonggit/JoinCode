namespace Core.Scheduling.Tasks;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(ITeammateExecutionMiddleware))]
public sealed partial class TeammateExecutionMiddleware : ITeammateExecutionMiddleware
{
    [Inject] private readonly IAgentLifecycleManager _agentLifecycleManager;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly ILogger<TeammateExecutionMiddleware>? _logger;
    [Inject] private readonly IClockService _clock;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct)
    {
        if (ctx.ContinuousModeHandled)
        {
            return;
        }

        try
        {
            var result = await _agentLifecycleManager.ExecuteAsync(ctx.Agent!, ct).ConfigureAwait(false);
            var elapsed = (long)(_clock.GetUtcNow() - ctx.StartTime).TotalMilliseconds;

            if (ctx.CleanupAsync is not null && ctx.State is not null)
            {
                await ctx.CleanupAsync(ctx.Definition.TeammateId, ctx.State).ConfigureAwait(false);
            }

            RecordTeammateMetrics("execute", result.IsSuccess);

            ctx.Result = result.IsSuccess
                ? AgentTaskResult.Success(ctx.Definition.TaskId, ctx.Definition.TeammateId, result.Output ?? string.Empty, elapsed)
                : AgentTaskResult.Failure(ctx.Definition.TaskId, ctx.Definition.TeammateId, result.Error ?? "Teammate execution failed", elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (ctx.TryCleanupAsync is not null)
            {
                await ctx.TryCleanupAsync(ctx.Definition.TeammateId).ConfigureAwait(false);
            }
            throw;
        }
        catch (Exception ex)
        {
            var elapsed = (long)(_clock.GetUtcNow() - ctx.StartTime).TotalMilliseconds;
            _logger?.LogError(ex, L.T(StringKey.InProcessTeammateFailedLog, ctx.Definition.TeammateId));

            if (ctx.TryCleanupAsync is not null)
            {
                await ctx.TryCleanupAsync(ctx.Definition.TeammateId).ConfigureAwait(false);
            }

            RecordTeammateMetrics("execute", false);
            ctx.Result = AgentTaskResult.Failure(ctx.Definition.TaskId, ctx.Definition.TeammateId, ex.Message, elapsed);
        }
    }

    private void RecordTeammateMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("scheduling.teammate.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "In-process teammate execution count");
}
