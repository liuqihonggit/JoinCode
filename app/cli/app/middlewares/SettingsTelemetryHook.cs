namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<SettingsContext>), ServiceLifetime.Singleton)]
internal sealed partial class SettingsTelemetryHook : TelemetryPostHook<SettingsContext> {
    /// <summary>初始化设置变更管道遥测后置钩子实例。</summary>
    public SettingsTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "settings.change.count", "Settings pipeline count") { }
}