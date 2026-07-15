using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Fork 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<ForkContext>))]
internal sealed partial class ForkTelemetryHook : IPipelinePostHook<ForkContext>
{
    private readonly ITelemetryService? _telemetryService;

    public ForkTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(ForkContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("agent.fork.count",
                new() { ["source"] = "pipeline" },
                "count", "Fork pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
