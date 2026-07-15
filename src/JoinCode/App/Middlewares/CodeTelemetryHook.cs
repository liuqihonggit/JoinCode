using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Code 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<CodeContext>))]
internal sealed partial class CodeTelemetryHook : IPipelinePostHook<CodeContext>
{
    private readonly ITelemetryService? _telemetryService;

    public CodeTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(CodeContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("code.index.count",
                new() { ["source"] = "pipeline" },
                "count", "Code pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
