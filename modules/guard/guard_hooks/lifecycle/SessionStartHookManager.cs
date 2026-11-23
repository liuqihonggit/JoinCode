namespace Core.Hooks.Lifecycle;

/// <summary>
/// 会话启动钩子管理器 — 在会话启动时编排 SessionStart 钩子,聚合阻断决策与附加配置
/// </summary>
[Register(typeof(ISessionStartHookManager), ServiceLifetime.Singleton)]
public sealed partial class SessionStartHookManager : ServiceEntity, ISessionStartHookManager {
    private readonly IHookOrchestrator _orchestrator;
    private readonly ILogger<SessionStartHookManager>? _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 初始化会话启动钩子管理器实例
    /// </summary>
    public SessionStartHookManager(IHookOrchestrator orchestrator, ILogger<SessionStartHookManager>? logger = null, ITelemetryService? telemetryService = null) {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger;
        _telemetryService = telemetryService;
    }

    /// <inheritdoc/>
    public async Task<SessionStartHookResult> OnSessionStartAsync(SessionStartHookContext context, CancellationToken ct = default) {
        var payload = new Dictionary<string, JsonElement> {
            ["sessionId"] = JsonElementHelper.FromString(context.SessionId),
            ["source"] = JsonElementHelper.FromString(context.Source),
            ["configuration"] = JsonSerializer.SerializeToElement(context.Configuration, HooksJsonContext.Default.DictionaryStringJsonElement)
        };

        var additionalConfig = new Dictionary<string, JsonElement>();
        // TASK130-A6 — 上下文类输出收集到类型化字段（此前塞进 AdditionalConfig 的字符串键，
        // 全仓无读取方且改键名不会编译失败）。
        var additionalContexts = new List<string>();
        string? initialUserMessage = null;

        await foreach (var result in _orchestrator.ExecuteHooksAsync(
            HookEvent.SessionStart,
            payload,
            matcher: context.Source,
            sessionId: context.SessionId,
            cancellationToken: ct).ConfigureAwait(false)) {
            if (result.Outcome == HookOutcome.Blocking) {
                _logger?.LogInformation("SessionStart hook blocked session {SessionId}: {Message}",
                    context.SessionId, result.Message);

                RecordHookMetrics(context.Source, true);
                return new SessionStartHookResult {
                    ShouldProceed = false,
                    Message = result.Message
                };
            }

            if (result.PreventContinuation) {
                return new SessionStartHookResult {
                    ShouldProceed = false,
                    Message = result.Message
                };
            }

            if (result.UpdatedInput != null) {
                foreach (var kvp in result.UpdatedInput) {
                    additionalConfig[kvp.Key] = kvp.Value;
                }
            }

            if (result.AdditionalContext is { Length: > 0 } additionalContext) {
                additionalContexts.Add(additionalContext);
            }

            if (result.InitialUserMessage is { Length: > 0 } initialUserMessageText) {
                initialUserMessage = initialUserMessageText;
            }
        }

        RecordHookMetrics(context.Source, false);

        return new SessionStartHookResult {
            AdditionalContext = additionalContexts.Count > 0 ? string.Join("\n\n", additionalContexts) : null,
            InitialUserMessage = initialUserMessage,
            AdditionalConfig = additionalConfig
        };
    }

    private void RecordHookMetrics(string source, bool isBlocked)
        => _telemetryService?.RecordCount("hook.sessionstart.count", new() { ["source"] = source, ["blocked"] = isBlocked.ToString() }, description: "Session start hook count");
}