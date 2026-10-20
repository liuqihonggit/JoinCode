namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<CodeContext>), ServiceLifetime.Singleton)]
internal sealed partial class CodeTelemetryHook : TelemetryPostHook<CodeContext> {
    /// <summary>构造函数 — 注入遥测服务,初始化 Code 索引管道计数指标</summary>
    public CodeTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "code.index.count", "Code pipeline count") { }
}