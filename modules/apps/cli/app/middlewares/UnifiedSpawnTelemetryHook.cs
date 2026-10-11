namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<UnifiedSpawnContext>), ServiceLifetime.Singleton)]
internal sealed partial class UnifiedSpawnTelemetryHook : TelemetryPostHook<UnifiedSpawnContext> {
    /// <summary>初始化统一子进程生成管道遥测后置钩子实例。</summary>
    public UnifiedSpawnTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "agent.spawn.count", "UnifiedSpawn pipeline count") { }
}