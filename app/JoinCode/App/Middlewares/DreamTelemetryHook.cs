using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<JoinCode.Dream.Pipeline.DreamContext>))]
internal sealed partial class DreamTelemetryHook : TelemetryPostHook<JoinCode.Dream.Pipeline.DreamContext>
{
    public DreamTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "dream.task.count", "Dream pipeline count") { }
}
