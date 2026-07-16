using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<Core.Context.ChatInitContext>))]
internal sealed partial class ChatInitTelemetryHook : TelemetryPostHook<Core.Context.ChatInitContext>
{
    public ChatInitTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "chat.init.count", "Chat initialization count",
            tagFactory: ctx => new() { ["source"] = ctx.SessionId != "default" ? "resume" : "startup" }) { }
}
