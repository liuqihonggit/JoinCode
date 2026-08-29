namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<Core.Context.ChatInitContext>), ServiceLifetime.Singleton)]
internal sealed partial class ChatInitTelemetryHook : TelemetryPostHook<Core.Context.ChatInitContext>
{
    public ChatInitTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "chat.init.count", "Chat initialization count",
            tagFactory: ctx => new() { ["source"] = ctx.SessionId != global::Core.Utils.SessionIdFactory.DefaultSessionId ? "resume" : "startup" }) { }
}
