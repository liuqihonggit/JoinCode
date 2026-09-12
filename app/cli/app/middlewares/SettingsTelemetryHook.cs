namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<SettingsContext>), ServiceLifetime.Singleton)]
internal sealed partial class SettingsTelemetryHook : TelemetryPostHook<SettingsContext>
{
    public SettingsTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "settings.change.count", "Settings pipeline count") { }
}
