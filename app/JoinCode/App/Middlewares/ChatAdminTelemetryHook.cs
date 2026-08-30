namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<Core.Context.ChatAdminContext>), ServiceLifetime.Singleton)]
internal sealed partial class ChatAdminTelemetryHook : TelemetryPostHook<Core.Context.ChatAdminContext>
{
    public ChatAdminTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "admin.operation.count", "Admin operation count",
            tagFactory: ctx => new() { ["operation"] = ctx.Operation.ToString() },
            condition: ctx => ctx.Error is null) { }
}
