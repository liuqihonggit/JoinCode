namespace Core.Context.Compact;

[Register(typeof(ICompactMiddleware))]
public sealed partial class CompactTelemetryMiddleware : ICompactMiddleware
{
    private readonly ITelemetryService? _telemetryService;

    public CompactTelemetryMiddleware(ITelemetryService? telemetryService = null)
    {
        _telemetryService = telemetryService;
    }

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(CompactContext context, MiddlewareDelegate<CompactContext> next, CancellationToken ct)
    {
        await next(context, ct).ConfigureAwait(false);

        if (_telemetryService is not null && context.Result is not null)
        {
            _telemetryService.RecordCount("compact.operation.count",
                new() { ["trigger"] = context.Request.Trigger.ToString(), ["level"] = context.Result.Level.ToString() },
                "count", "Compact operation count");
        }
    }
}
