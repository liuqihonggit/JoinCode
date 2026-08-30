namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<ForkContext>), ServiceLifetime.Singleton)]
internal sealed partial class ForkTelemetryHook : TelemetryPostHook<ForkContext>
{
    public ForkTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "agent.fork.count", "Fork pipeline count") { }
}
