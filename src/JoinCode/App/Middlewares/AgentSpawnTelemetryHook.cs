using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// AgentSpawn 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<AgentSpawnContext>))]
internal sealed class AgentSpawnTelemetryHook : IPipelinePostHook<AgentSpawnContext>
{
    private readonly ITelemetryService? _telemetryService;

    public AgentSpawnTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(AgentSpawnContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("agent.spawn.count",
                new() { ["source"] = "pipeline" },
                "count", "AgentSpawn pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
