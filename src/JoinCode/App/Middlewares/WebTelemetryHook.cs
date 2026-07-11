using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Web 管道 Post Hook — 遥测记录执行计数
/// </summary>
[Register(typeof(IPipelinePostHook<WebContext>))]
internal sealed class WebTelemetryHook : IPipelinePostHook<WebContext>
{
    private readonly ITelemetryService? _telemetryService;

    public WebTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(WebContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("web.fetch.count",
                new() { ["source"] = "pipeline" },
                "count", "Web pipeline count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
