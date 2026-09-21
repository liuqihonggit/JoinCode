namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<PreprocessContext>), ServiceLifetime.Singleton)]
internal sealed partial class PreprocessTelemetryHook : TelemetryPostHook<PreprocessContext> {
    /// <summary>初始化预处理管道遥测后置钩子实例。</summary>
    public PreprocessTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "preprocess.count", "Preprocess pipeline count") { }
}