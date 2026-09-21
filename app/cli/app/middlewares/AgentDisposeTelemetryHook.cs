namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<AgentDisposeContext>), ServiceLifetime.Singleton)]
internal sealed partial class AgentDisposeTelemetryHook : TelemetryPostHook<AgentDisposeContext> {
    /// <summary>构造函数 — 注入遥测服务,初始化 AgentDispose 管道计数指标</summary>
    public AgentDisposeTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "agent.dispose.count", "AgentDispose pipeline count") { }
}