using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<AgentSpawnContext>))]
internal sealed partial class AgentSpawnTelemetryHook : TelemetryPostHook<AgentSpawnContext>
{
    public AgentSpawnTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "agent.spawn.count", "AgentSpawn pipeline count") { }
}
