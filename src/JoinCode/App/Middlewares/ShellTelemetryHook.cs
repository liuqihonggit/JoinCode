using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Shell 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<ShellContext>))]
internal sealed partial class ShellTelemetryHook : IPipelinePostHook<ShellContext>
{
    private readonly ITelemetryService? _telemetryService;

    public ShellTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(ShellContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("shell.execute.count",
                new() { ["source"] = "pipeline" },
                "count", "Shell pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
