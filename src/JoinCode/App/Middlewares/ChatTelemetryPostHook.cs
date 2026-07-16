using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Pipeline;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Chat 管道 Post Hook — 遥测 Dispose Span + 指标记录
/// </summary>
[Register(typeof(IPipelinePostHook<Core.Context.ChatMiddlewareContext>))]
internal sealed partial class ChatTelemetryPostHook : IPipelinePostHook<Core.Context.ChatMiddlewareContext>
{
    private readonly ITelemetryService? _telemetryService;

    public ChatTelemetryPostHook(ITelemetryService? telemetryService)
    {
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(Core.Context.ChatMiddlewareContext context, CancellationToken ct)
    {
        if (_telemetryService is null)
            return;

        context.Span?.SetTag("chat.tool_calls", context.TotalToolCalls);
        context.Span?.SetTag("chat.prompt_tokens", context.FinalUsage?.PromptTokens ?? 0);
        context.Span?.SetTag("chat.completion_tokens", context.FinalUsage?.CompletionTokens ?? 0);
        context.Span?.SetTag("chat.cache_read_tokens", context.FinalUsage?.CacheReadInputTokens ?? 0);
        context.Span?.SetTag("chat.cache_creation_tokens", context.FinalUsage?.CacheCreationInputTokens ?? 0);
        context.Span?.SetTag("chat.model", context.FinalModelId ?? "unknown");
        context.Span?.SetStatus(TelemetryStatusCode.Ok);
        if (context.Span is not null) await context.Span.DisposeAsync().ConfigureAwait(false);

        _telemetryService.RecordCount("chat.send.count", new() { ["mode"] = "events" }, "count", "Chat message send count");
        if (context.FinalUsage is not null)
        {
            var tokenCounter = _telemetryService.GetCounter("chat.send.tokens", "tokens", "Chat token usage");
            tokenCounter.Add(context.FinalUsage.PromptTokens, new Dictionary<string, string> { ["mode"] = "events", ["type"] = "prompt" });
            tokenCounter.Add(context.FinalUsage.CompletionTokens, new Dictionary<string, string> { ["mode"] = "events", ["type"] = "completion" });
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
