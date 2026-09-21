namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<JoinCode.Dream.Pipeline.DreamContext>), ServiceLifetime.Singleton)]
internal sealed partial class DreamTelemetryHook : TelemetryPostHook<JoinCode.Dream.Pipeline.DreamContext> {
    /// <summary>构造函数 — 注入遥测服务,初始化 Dream 管道计数指标</summary>
    public DreamTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "dream.task.count", "Dream pipeline count") { }
}