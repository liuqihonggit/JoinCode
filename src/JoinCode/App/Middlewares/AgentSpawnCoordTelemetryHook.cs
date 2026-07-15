using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// AgentSpawnCoord 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<AgentSpawnCoordContext>))]
internal sealed partial class AgentSpawnCoordTelemetryHook : IPipelinePostHook<AgentSpawnCoordContext>
{
    private readonly ITelemetryService? _telemetryService;

    public AgentSpawnCoordTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(AgentSpawnCoordContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("agent.spawn.coord.count",
                new() { ["source"] = "pipeline" },
                "count", "AgentSpawnCoord pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
