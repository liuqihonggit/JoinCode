namespace JoinCode.App.Middlewares;

/// <summary>
/// Chat 管道 Pre Hook — 遥测 StartSpan + UserPromptSubmit Hook 拦截
/// </summary>
[Register(typeof(IPipelinePreHook<Core.Context.ChatMiddlewareContext>), ServiceLifetime.Singleton)]
internal sealed partial class ChatTelemetryPreHook : ServiceEntity, IPipelinePreHook<Core.Context.ChatMiddlewareContext> {
    private readonly ITelemetryService? _telemetryService;
    private readonly IHookOrchestrator? _hookOrchestrator;
    private readonly ILogger<ChatTelemetryPreHook>? _logger;

    /// <summary>构造函数 — 注入遥测服务、Hook 编排器和日志器,用于 Chat 管道前置遥测与 Hook 拦截</summary>
    public ChatTelemetryPreHook(
        ITelemetryService? telemetryService,
        IHookOrchestrator? hookOrchestrator,
        ILogger<ChatTelemetryPreHook>? logger) {
        _telemetryService = telemetryService;
        _hookOrchestrator = hookOrchestrator;
        _logger = logger;
    }

    /// <summary>Chat 管道前置钩子 — 启动遥测 Span 并执行 UserPromptSubmit Hook 拦截,返回是否继续管道</summary>
    public async Task<bool> InvokeAsync(Core.Context.ChatMiddlewareContext context, CancellationToken ct) {
        // 1. 遥测: StartSpan
        if (_telemetryService is not null) {
            var span = _telemetryService.StartSpan(context.SpanName, TelemetrySpanKind.Server);
            span.SetTag("chat.message_length", context.Message.Length);
            context.Span = span;
        }

        // 2. UserPromptSubmit Hook — 对齐 TS processUserInput.ts:182
        // TASK071 hooks-③ — sessionId 用 context.SessionId（旧硬编码 "unknown" 致会话级钩子永不匹配）；
        // hook 结果的 additionalContext 拼接注入对话（对齐 TS additionalContext 注入语义）
        if (_hookOrchestrator is not null) {
            var payload = new Dictionary<string, JsonElement> {
                ["prompt"] = JsonElementHelper.FromString(context.Message),
                ["session_id"] = JsonElementHelper.FromString(context.SessionId),
            };

            var additionalContexts = new List<string>();
            await foreach (var result in _hookOrchestrator.ExecuteHooksAsync(
                HookEvent.UserPromptSubmit,
                payload,
                sessionId: context.SessionId,
                cancellationToken: ct).ConfigureAwait(false)) {
                if (result.Outcome == HookOutcome.Blocking) {
                    _logger?.LogWarning("[ChatPipeline] UserPromptSubmit Hook 阻止了请求: {Message}", result.Message);
                    return false;
                }

                if (result.PreventContinuation) {
                    return false;
                }

                if (!string.IsNullOrEmpty(result.AdditionalContext)) {
                    additionalContexts.Add(result.AdditionalContext);
                }
            }

            if (additionalContexts.Count > 0) {
                context.UserPromptSubmitInjection = string.Join(Environment.NewLine, additionalContexts);
            }
        }

        return true;
    }
}