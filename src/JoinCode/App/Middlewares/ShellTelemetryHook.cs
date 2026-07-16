using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<ShellContext>))]
internal sealed partial class ShellTelemetryHook : TelemetryPostHook<ShellContext>
{
    public ShellTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "shell.execute.count", "Shell pipeline count") { }
}
