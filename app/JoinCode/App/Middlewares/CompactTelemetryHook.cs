using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<CompactContext>))]
internal sealed partial class CompactTelemetryHook : TelemetryPostHook<CompactContext>
{
    public CompactTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "compact.count", "Compact pipeline count") { }
}
