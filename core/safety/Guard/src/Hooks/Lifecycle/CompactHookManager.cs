namespace Core.Hooks.Lifecycle;

[Register]
public sealed partial class CompactHookManager : ICompactHookManager
{
    private readonly IHookOrchestrator _orchestrator;
    [Inject] private readonly ILogger<CompactHookManager>? _logger;
    private readonly ITelemetryService? _telemetryService;

    public CompactHookManager(IHookOrchestrator orchestrator, ILogger<CompactHookManager>? logger = null, ITelemetryService? telemetryService = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public async Task<CompactHookResult> OnPreCompactAsync(CompactHookContext context, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, JsonElement>
        {
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
            cancellationToken: ct).ConfigureAwait(false))
        {
            if (result.Outcome == HookOutcome.Blocking)
            {
                _logger?.LogInformation("PreCompact hook blocked compression for session {SessionId}: {Message}",
                    context.SessionId, result.Message);

                return new CompactHookResult
                {
                    ShouldCompact = false,
                    Message = result.Message,
                    Action = CompactHookAction.Skip
                };
            }

            if (result.PreventContinuation)
            {
                return new CompactHookResult
                {
                    ShouldCompact = false,
                    Message = result.Message,
                    Action = CompactHookAction.Defer
                };
            }

            if (result.UpdatedInput != null)
            {
                if (result.UpdatedInput.TryGetValue("action", out var actionElement) &&
                    actionElement.ValueKind == JsonValueKind.String &&
                    actionElement.GetString() is string actionStr)
                {
                    var customAction = CompactHookActionExtensions.FromValue(actionStr);
                    if (customAction is not null)
                    {
                        return new CompactHookResult
                        {
                            ShouldCompact = customAction.Value == CompactHookAction.Proceed,
                            Message = result.Message,
                            Action = customAction.Value
                        };
                    }
                }
            }
        }

        return new CompactHookResult();
    }

    public async Task OnPostCompactAsync(CompactHookContext context, PostCompactData result, CancellationToken ct = default)
    {
        RecordCompactMetrics(context.Trigger, result.Compacted, result.PreCompactTokenCount - result.PostCompactTokenCount);

        var payload = new Dictionary<string, JsonElement>
        {
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
            cancellationToken: ct).ConfigureAwait(false))
        {
            if (hookResult.Outcome == HookOutcome.NonBlockingError)
            {
                _logger?.LogWarning("PostCompact hook error for session {SessionId}: {Message}",
                    context.SessionId, hookResult.Message);
            }
        }
    }

    private void RecordCompactMetrics(string trigger, bool compacted, int tokensSaved)
    {
        _telemetryService?.RecordCount("hook.compact.count", new() { ["trigger"] = trigger, ["compacted"] = compacted.ToString() }, description: "Compact hook execution count");
        if (compacted)
        {
            _telemetryService?.RecordHistogram("hook.compact.tokens.saved", tokensSaved, new() { ["trigger"] = trigger }, "tokens", "Tokens saved by compaction");
        }
    }
}
