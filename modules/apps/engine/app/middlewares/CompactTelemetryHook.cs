namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<CompactContext>), ServiceLifetime.Singleton)]
internal sealed partial class CompactTelemetryHook : TelemetryPostHook<CompactContext> {
    /// <summary>构造函数 — 注入遥测服务,初始化 Compact 管道计数指标</summary>
    public CompactTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "compact.count", "Compact pipeline count") { }
}