using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// AgentDispose 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<AgentDisposeContext>))]
internal sealed class AgentDisposeTelemetryHook : IPipelinePostHook<AgentDisposeContext>
{
    private readonly ITelemetryService? _telemetryService;

    public AgentDisposeTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(AgentDisposeContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("agent.dispose.count",
                new() { ["source"] = "pipeline" },
                "count", "AgentDispose pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
