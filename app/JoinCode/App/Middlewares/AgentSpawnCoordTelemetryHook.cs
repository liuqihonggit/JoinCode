using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<AgentSpawnCoordContext>))]
internal sealed partial class AgentSpawnCoordTelemetryHook : TelemetryPostHook<AgentSpawnCoordContext>
{
    public AgentSpawnCoordTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "agent.spawn.coord.count", "AgentSpawnCoord pipeline count") { }
}
