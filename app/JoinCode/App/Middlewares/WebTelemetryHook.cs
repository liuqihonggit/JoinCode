using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<WebContext>))]
internal sealed partial class WebTelemetryHook : TelemetryPostHook<WebContext>
{
    public WebTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "web.fetch.count", "Web pipeline count") { }
}
