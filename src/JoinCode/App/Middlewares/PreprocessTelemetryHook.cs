using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Preprocess 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<PreprocessContext>))]
internal sealed class PreprocessTelemetryHook : IPipelinePostHook<PreprocessContext>
{
    private readonly ITelemetryService? _telemetryService;

    public PreprocessTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(PreprocessContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("preprocess.count",
                new() { ["source"] = "pipeline" },
                "count", "Preprocess pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
