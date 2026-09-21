namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<ForkContext>), ServiceLifetime.Singleton)]
internal sealed partial class ForkTelemetryHook : TelemetryPostHook<ForkContext> {
    /// <summary>构造函数 — 注入遥测服务,初始化 Fork 管道计数指标</summary>
    public ForkTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "agent.fork.count", "Fork pipeline count") { }
}