using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<QueryMiddlewareContext>))]
internal sealed partial class QueryTelemetryHook : TelemetryPostHook<QueryMiddlewareContext>
{
    public QueryTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "query.count", "Query pipeline count") { }
}
