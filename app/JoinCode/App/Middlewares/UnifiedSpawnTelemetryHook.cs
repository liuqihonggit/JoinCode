using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<UnifiedSpawnContext>), ServiceLifetime.Singleton)]
internal sealed partial class UnifiedSpawnTelemetryHook : TelemetryPostHook<UnifiedSpawnContext>
{
    public UnifiedSpawnTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "agent.spawn.count", "UnifiedSpawn pipeline count") { }
}
