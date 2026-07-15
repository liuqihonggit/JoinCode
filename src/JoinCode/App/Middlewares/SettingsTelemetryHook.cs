using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Settings 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<SettingsContext>))]
internal sealed partial class SettingsTelemetryHook : IPipelinePostHook<SettingsContext>
{
    private readonly ITelemetryService? _telemetryService;

    public SettingsTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(SettingsContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("settings.change.count",
                new() { ["source"] = "pipeline" },
                "count", "Settings pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
