namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<CompactContext>), ServiceLifetime.Singleton)]
internal sealed partial class CompactTelemetryHook : TelemetryPostHook<CompactContext>
{
    public CompactTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "compact.count", "Compact pipeline count") { }
}
