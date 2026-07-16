using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<PermissionCheckContext>))]
internal sealed partial class PermissionCheckTelemetryHook : TelemetryPostHook<PermissionCheckContext>
{
    public PermissionCheckTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "permission.check.count", "PermissionCheck pipeline count") { }
}
