namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<ShellPipelineContext>), ServiceLifetime.Singleton)]
internal sealed partial class ShellTelemetryHook : TelemetryPostHook<ShellPipelineContext>
{
    public ShellTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "shell.execute.count", "Shell pipeline count") { }
}
