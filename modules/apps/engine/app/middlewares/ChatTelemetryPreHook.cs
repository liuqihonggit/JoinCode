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
        // TASK130-A5 — 执行逻辑抽到 UserPromptSubmitHookRunner，与主代理路径共用同一实现
        if (_hookOrchestrator is not null) {
            var outcome = await UserPromptSubmitHookRunner.RunAsync(
                _hookOrchestrator, context.Message, context.SessionId, ct).ConfigureAwait(false);

            if (outcome.Blocked) {
                _logger?.LogWarning("[ChatPipeline] UserPromptSubmit Hook 阻止了请求（session={SessionId}）", context.SessionId);
                return false;
            }

            if (outcome.AdditionalContext is { } injection) {
                context.UserPromptSubmitInjection = injection;
            }

            if (outcome.SystemNotices.Count > 0) {
                context.UserPromptSubmitSystemNotices = outcome.SystemNotices;
            }
        }

        return true;
    }
}