using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Dream 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<JoinCode.Dream.Pipeline.DreamContext>))]
internal sealed partial class DreamTelemetryHook : IPipelinePostHook<JoinCode.Dream.Pipeline.DreamContext>
{
    private readonly ITelemetryService? _telemetryService;

    public DreamTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(JoinCode.Dream.Pipeline.DreamContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("dream.task.count",
                new() { ["source"] = "pipeline" },
                "count", "Dream pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
