using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<SettingsContext>))]
internal sealed partial class SettingsTelemetryHook : TelemetryPostHook<SettingsContext>
{
    public SettingsTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "settings.change.count", "Settings pipeline count") { }
}
