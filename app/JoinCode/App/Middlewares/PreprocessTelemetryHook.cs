namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<PreprocessContext>), ServiceLifetime.Singleton)]
internal sealed partial class PreprocessTelemetryHook : TelemetryPostHook<PreprocessContext>
{
    public PreprocessTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "preprocess.count", "Preprocess pipeline count") { }
}
