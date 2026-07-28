using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<PreprocessContext>))]
internal sealed partial class PreprocessTelemetryHook : TelemetryPostHook<PreprocessContext>
{
    public PreprocessTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "preprocess.count", "Preprocess pipeline count") { }
}
