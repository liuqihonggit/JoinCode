using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<ForkContext>))]
internal sealed partial class ForkTelemetryHook : TelemetryPostHook<ForkContext>
{
    public ForkTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "agent.fork.count", "Fork pipeline count") { }
}
