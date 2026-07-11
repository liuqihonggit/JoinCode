using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// ChatInit 管道 Post Hook — 遥测记录初始化计数
/// </summary>
[Register(typeof(IPipelinePostHook<Core.Context.ChatInitContext>))]
internal sealed class ChatInitTelemetryHook : IPipelinePostHook<Core.Context.ChatInitContext>
{
    private readonly ITelemetryService? _telemetryService;

    public ChatInitTelemetryHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(Core.Context.ChatInitContext context, CancellationToken ct)
    {
        if (_telemetryService is not null)
        {
            _telemetryService.RecordCount("chat.init.count",
                new() { ["source"] = context.SessionId != "default" ? "resume" : "startup" },
                "count", "Chat initialization count");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
