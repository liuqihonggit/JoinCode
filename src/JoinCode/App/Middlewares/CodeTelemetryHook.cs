using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<CodeContext>))]
internal sealed partial class CodeTelemetryHook : TelemetryPostHook<CodeContext>
{
    public CodeTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "code.index.count", "Code pipeline count") { }
}
