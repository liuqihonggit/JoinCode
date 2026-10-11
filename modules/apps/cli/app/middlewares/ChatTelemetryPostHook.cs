namespace JoinCode.App.Middlewares;

/// <summary>
/// Chat 管道 Post Hook — 遥测 Dispose Span + 指标记录
/// </summary>
[Register(typeof(IPipelinePostHook<Core.Context.ChatMiddlewareContext>), ServiceLifetime.Singleton)]
internal sealed partial class ChatTelemetryPostHook : ServiceEntity, IPipelinePostHook<Core.Context.ChatMiddlewareContext> {
    private readonly ITelemetryService? _telemetryService;

    /// <summary>构造函数 — 注入遥测服务,用于记录 Chat 管道后置遥测指标</summary>
    public ChatTelemetryPostHook(ITelemetryService? telemetryService) {
        _telemetryService = telemetryService;
    }

    /// <summary>Chat 管道后置钩子 — 设置 Span 标签与状态、释放 Span,并记录消息发送计数与 Token 用量指标</summary>
    public async Task InvokeAsync(Core.Context.ChatMiddlewareContext context, CancellationToken ct) {
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
        if (context.FinalUsage is not null) {
            var tokenCounter = _telemetryService.GetCounter("chat.send.tokens", "tokens", "Chat token usage");
            tokenCounter.Add(context.FinalUsage.PromptTokens, new Dictionary<string, string> { ["mode"] = "events", ["type"] = "prompt" });
            tokenCounter.Add(context.FinalUsage.CompletionTokens, new Dictionary<string, string> { ["mode"] = "events", ["type"] = "completion" });
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}