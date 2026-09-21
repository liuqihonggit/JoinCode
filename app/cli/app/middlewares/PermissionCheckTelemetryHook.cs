namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<PermissionCheckContext>), ServiceLifetime.Singleton)]
internal sealed partial class PermissionCheckTelemetryHook : TelemetryPostHook<PermissionCheckContext> {
    /// <summary>构造函数 — 注入遥测服务,初始化 PermissionCheck 管道计数指标</summary>
    public PermissionCheckTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "permission.check.count", "PermissionCheck pipeline count") { }
}