namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<Core.Context.ChatAdminContext>), ServiceLifetime.Singleton)]
internal sealed partial class ChatAdminTelemetryHook : TelemetryPostHook<Core.Context.ChatAdminContext> {
    /// <summary>构造函数 — 注入遥测服务,初始化 Admin 操作计数指标,按操作类型打标签且仅在成功时记录</summary>
    public ChatAdminTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "admin.operation.count", "Admin operation count",
            tagFactory: ctx => new() { ["operation"] = ctx.Operation.ToString() },
            condition: ctx => ctx.Error is null) { }
}