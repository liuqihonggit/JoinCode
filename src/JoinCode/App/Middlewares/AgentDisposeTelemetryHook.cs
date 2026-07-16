using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<AgentDisposeContext>))]
internal sealed partial class AgentDisposeTelemetryHook : TelemetryPostHook<AgentDisposeContext>
{
    public AgentDisposeTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "agent.dispose.count", "AgentDispose pipeline count") { }
}
