using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// PermissionCheck 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<PermissionCheckContext>))]
internal sealed partial class PermissionCheckTelemetryHook : IPipelinePostHook<PermissionCheckContext>
{
    private readonly ITelemetryService? _telemetryService;

    public PermissionCheckTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(PermissionCheckContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("permission.check.count",
                new() { ["source"] = "pipeline" },
                "count", "PermissionCheck pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
