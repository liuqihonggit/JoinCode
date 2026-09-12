namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<PermissionCheckContext>), ServiceLifetime.Singleton)]
internal sealed partial class PermissionCheckTelemetryHook : TelemetryPostHook<PermissionCheckContext>
{
    public PermissionCheckTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "permission.check.count", "PermissionCheck pipeline count") { }
}
