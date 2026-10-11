namespace Core.Hooks.Lifecycle;

/// <summary>
/// 紧凑模式钩子管理器实现 — 在上下文压缩前后触发已注册的 PreCompact/PostCompact 钩子,支持阻塞、延迟与自定义动作
/// </summary>
[Register(typeof(ICompactHookManager), ServiceLifetime.Singleton)]
public sealed partial class CompactHookManager : ServiceEntity, ICompactHookManager {
    private readonly IHookOrchestrator _orchestrator;
    private readonly ILogger<CompactHookManager>? _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造紧凑模式钩子管理器
    /// </summary>
    /// <param name="orchestrator">钩子编排器,用于执行匹配的钩子</param>
    /// <param name="logger">日志记录器(可选)</param>
    /// <param name="telemetryService">遥测服务(可选)</param>
    public CompactHookManager(IHookOrchestrator orchestrator, ILogger<CompactHookManager>? logger = null, ITelemetryService? telemetryService = null) {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger;
        _telemetryService = telemetryService;
    }

    /// <inheritdoc/>
    public async Task<CompactHookResult> OnPreCompactAsync(CompactHookContext context, CancellationToken ct = default) {
        var payload = new Dictionary<string, JsonElement> {
            ["sessionId"] = JsonElementHelper.FromString(context.SessionId),
            ["trigger"] = JsonElementHelper.FromString(context.Trigger),
            ["currentTokenCount"] = JsonElementHelper.FromInt32(context.CurrentTokenCount),
            ["targetTokenCount"] = JsonElementHelper.FromInt32(context.TargetTokenCount),
            ["metadata"] = JsonSerializer.SerializeToElement(context.Metadata, HooksJsonContext.Default.DictionaryStringJsonElement)
        };

        await foreach (var result in _orchestrator.ExecuteHooksAsync(
            HookEvent.PreCompact,
            payload,
            matcher: context.Trigger,
            sessionId: context.SessionId,
            cancellationToken: ct).ConfigureAwait(false)) {
            if (result.Outcome == HookOutcome.Blocking) {
                _logger?.LogInformation("PreCompact hook blocked compression for session {SessionId}: {Message}",
                    context.SessionId, result.Message);

                return new CompactHookResult {
                    ShouldCompact = false,
                    Message = result.Message,
                    Action = CompactHookAction.Skip
                };
            }

            if (result.PreventContinuation) {
                return new CompactHookResult {
                    ShouldCompact = false,
                    Message = result.Message,
                    Action = CompactHookAction.Defer
                };
            }

            if (result.UpdatedInput != null &&
                result.UpdatedInput.TryGetValue("action", out var actionElement) &&
                actionElement.ValueKind == JsonValueKind.String &&
                actionElement.GetString() is string actionStr &&
                CompactHookActionExtensions.FromValue(actionStr) is { } customAction) {
                return new CompactHookResult {
                    ShouldCompact = customAction == CompactHookAction.Proceed,
                    Message = result.Message,
                    Action = customAction
                };
            }
        }

        return new CompactHookResult();
    }

    /// <inheritdoc/>
    public async Task OnPostCompactAsync(CompactHookContext context, PostCompactData result, CancellationToken ct = default) {
        RecordCompactMetrics(context.Trigger, result.Compacted, result.PreCompactTokenCount - result.PostCompactTokenCount);

        var payload = new Dictionary<string, JsonElement> {
            ["sessionId"] = JsonElementHelper.FromString(context.SessionId),
            ["trigger"] = JsonElementHelper.FromString(context.Trigger),
            ["compacted"] = JsonElementHelper.FromBoolean(result.Compacted),
            ["level"] = JsonElementHelper.FromString(result.Level),
            ["preCompactTokenCount"] = JsonElementHelper.FromInt32(result.PreCompactTokenCount),
            ["postCompactTokenCount"] = JsonElementHelper.FromInt32(result.PostCompactTokenCount),
            ["messagesRemoved"] = JsonElementHelper.FromInt32(result.MessagesRemoved),
            ["messagesPreserved"] = JsonElementHelper.FromInt32(result.MessagesPreserved),
            ["summary"] = JsonElementHelper.FromString(result.Summary)
        };

        await foreach (var hookResult in _orchestrator.ExecuteHooksAsync(
            HookEvent.PostCompact,
            payload,
            matcher: context.Trigger,
            sessionId: context.SessionId,
            cancellationToken: ct).ConfigureAwait(false)) {
            if (hookResult.Outcome == HookOutcome.NonBlockingError) {
                _logger?.LogWarning("PostCompact hook error for session {SessionId}: {Message}",
                    context.SessionId, hookResult.Message);
            }
        }
    }

    private void RecordCompactMetrics(string trigger, bool compacted, int tokensSaved) {
        _telemetryService?.RecordCount("hook.compact.count", new() { ["trigger"] = trigger, ["compacted"] = compacted.ToString() }, description: "Compact hook execution count");
        if (compacted) {
            _telemetryService?.RecordHistogram("hook.compact.tokens.saved", tokensSaved, new() { ["trigger"] = trigger }, "tokens", "Tokens saved by compaction");
        }
    }
}