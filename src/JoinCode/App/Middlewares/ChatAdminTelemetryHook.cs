using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// ChatAdmin 管道 Post Hook — 遥测记录管理操作计数
/// </summary>
[Register(typeof(IPipelinePostHook<Core.Context.ChatAdminContext>))]
internal sealed class ChatAdminTelemetryHook : IPipelinePostHook<Core.Context.ChatAdminContext>
{
    private readonly ITelemetryService? _telemetryService;

    public ChatAdminTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(Core.Context.ChatAdminContext context, CancellationToken ct)
    {
        if (_telemetryService is not null && context.Error is null)
        {
            _telemetryService.RecordCount("admin.operation.count",
                new() { ["operation"] = context.Operation.ToString() },
                "count", "Admin operation count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
