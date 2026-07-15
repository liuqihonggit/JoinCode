using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Query 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<QueryMiddlewareContext>))]
internal sealed partial class QueryTelemetryHook : IPipelinePostHook<QueryMiddlewareContext>
{
    private readonly ITelemetryService? _telemetryService;

    public QueryTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(QueryMiddlewareContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("query.count",
                new() { ["source"] = "pipeline" },
                "count", "Query pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
