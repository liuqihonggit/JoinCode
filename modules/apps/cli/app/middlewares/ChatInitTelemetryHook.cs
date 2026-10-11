namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<Core.Context.ChatInitContext>), ServiceLifetime.Singleton)]
internal sealed partial class ChatInitTelemetryHook : TelemetryPostHook<Core.Context.ChatInitContext> {
    /// <summary>构造函数 — 注入遥测服务,初始化 Chat 初始化计数指标,按会话来源(启动/恢复)打标签</summary>
    public ChatInitTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "chat.init.count", "Chat initialization count",
            tagFactory: ctx => new() { ["source"] = ctx.SessionId != global::Core.Utils.SessionIdFactory.DefaultSessionId ? "resume" : "startup" }) { }
}