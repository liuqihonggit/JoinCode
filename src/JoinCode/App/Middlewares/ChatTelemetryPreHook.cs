using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;
using Core.Hooks;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Chat 管道 Pre Hook — 遥测 StartSpan + UserPromptSubmit Hook 拦截
/// </summary>
[Register(typeof(IPipelinePreHook<Core.Context.ChatMiddlewareContext>))]
internal sealed class ChatTelemetryPreHook : IPipelinePreHook<Core.Context.ChatMiddlewareContext>
{
    private readonly ITelemetryService? _telemetryService;
    private readonly IHookOrchestrator? _hookOrchestrator;
    private readonly ILogger<ChatTelemetryPreHook>? _logger;

    public ChatTelemetryPreHook(
        ITelemetryService? telemetryService,
        IHookOrchestrator? hookOrchestrator,
        ILogger<ChatTelemetryPreHook>? logger)
    {
        _telemetryService = telemetryService;
        _hookOrchestrator = hookOrchestrator;
        _logger = logger;
    }

    public async Task<bool> InvokeAsync(Core.Context.ChatMiddlewareContext context, CancellationToken ct)
    {
        // 1. 遥测: StartSpan
        if (_telemetryService is not null)
        {
            var span = _telemetryService.StartSpan(context.SpanName, TelemetrySpanKind.Server);
            span.SetTag("chat.message_length", context.Message.Length);
            context.Span = span;
        }

        // 2. UserPromptSubmit Hook — 对齐 TS processUserInput.ts:182
        if (_hookOrchestrator is not null)
        {
            var payload = new Dictionary<string, JsonElement>
            {
                ["prompt"] = JsonElementHelper.FromString(context.Message),
                ["session_id"] = JsonElementHelper.FromString("unknown")
            };

            await foreach (var result in _hookOrchestrator.ExecuteHooksAsync(
                HookEvent.UserPromptSubmit,
                payload,
                sessionId: "unknown",
                cancellationToken: ct).ConfigureAwait(false))
            {
                if (result.Outcome == HookOutcome.Blocking)
                {
                    _logger?.LogWarning("[ChatPipeline] UserPromptSubmit Hook 阻止了请求: {Message}", result.Message);
                    return false;
                }

                if (result.PreventContinuation)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
