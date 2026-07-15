using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Compact 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<CompactContext>))]
internal sealed partial class CompactTelemetryHook : IPipelinePostHook<CompactContext>
{
    private readonly ITelemetryService? _telemetryService;

    public CompactTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(CompactContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("compact.count",
                new() { ["source"] = "pipeline" },
                "count", "Compact pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
