using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

internal abstract class TelemetryPostHook<TContext> : IPipelinePostHook<TContext>
{
    private readonly ITelemetryService? _telemetryService;
    private readonly string _metricName;
    private readonly string _description;
    private readonly Func<TContext, Dictionary<string, string>>? _tagFactory;
    private readonly Func<TContext, bool>? _condition;

    protected TelemetryPostHook(
        ITelemetryService? telemetryService,
        string metricName,
        string description,
        Func<TContext, Dictionary<string, string>>? tagFactory = null,
        Func<TContext, bool>? condition = null)
    {
        _telemetryService = telemetryService;
        _metricName = metricName;
        _description = description;
        _tagFactory = tagFactory;
        _condition = condition;
    }

    public async Task InvokeAsync(TContext context, CancellationToken ct)
    {
        if (_telemetryService is not null && (_condition?.Invoke(context) ?? true))
        {
            var tags = _tagFactory?.Invoke(context) ?? new Dictionary<string, string> { ["source"] = "pipeline" };
            _telemetryService.RecordCount(_metricName, tags, "count", _description);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
