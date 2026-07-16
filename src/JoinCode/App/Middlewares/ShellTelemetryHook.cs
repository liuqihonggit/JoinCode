using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<ShellPipelineContext>))]
internal sealed partial class ShellTelemetryHook : TelemetryPostHook<ShellPipelineContext>
{
    public ShellTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "shell.execute.count", "Shell pipeline count") { }
}
