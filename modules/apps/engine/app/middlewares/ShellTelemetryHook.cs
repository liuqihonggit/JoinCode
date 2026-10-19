namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<ShellPipelineContext>), ServiceLifetime.Singleton)]
internal sealed partial class ShellTelemetryHook : TelemetryPostHook<ShellPipelineContext> {
    /// <summary>初始化 Shell 执行管道遥测后置钩子实例。</summary>
    public ShellTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "shell.execute.count", "Shell pipeline count") { }
}