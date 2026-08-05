namespace Core.Hooks.Lifecycle;

[Register]
public sealed partial class SubagentStopHookManager : ServiceEntity, ISubagentStopHookManager
{
    private readonly IHookOrchestrator _orchestrator;
    [Inject] private readonly ILogger<SubagentStopHookManager>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private static readonly TimeSpan HookTimeout = TimeSpan.FromSeconds(60);

    public SubagentStopHookManager(IHookOrchestrator orchestrator, ILogger<SubagentStopHookManager>? logger = null, ITelemetryService? telemetryService = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public async Task<SubagentStopHookResult> OnSubagentStopAsync(SubagentStopHookContext context, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(HookTimeout);

        var payload = new Dictionary<string, JsonElement>
        {
            ["sessionId"] = JsonElementHelper.FromString(context.SessionId),
            ["agentId"] = JsonElementHelper.FromString(context.AgentId),
            ["agentType"] = JsonElementHelper.FromString(context.AgentType),
            ["isSuccess"] = JsonElementHelper.FromBoolean(context.IsSuccess),
            ["error"] = JsonElementHelper.FromString(context.Error ?? string.Empty),
            ["executionTimeMs"] = JsonElementHelper.FromInt64(context.ExecutionTimeMs ?? 0),
        };

        if (context.WorktreePath is not null)
        {
            payload["worktreePath"] = JsonElementHelper.FromString(context.WorktreePath);
        }

        if (context.Metadata.Count > 0)
        {
            payload["metadata"] = JsonSerializer.SerializeToElement(context.Metadata, HooksJsonContext.Default.DictionaryStringJsonElement);
        }

        var additionalData = new Dictionary<string, JsonElement>();
        var wasBlocked = false;

        try
        {
            await foreach (var result in _orchestrator.ExecuteHooksAsync(
                HookEvent.SubagentStop,
                payload,
                matcher: context.AgentType,
                sessionId: context.SessionId,
                cancellationToken: timeoutCts.Token).ConfigureAwait(false))
            {
                if (result.Outcome == HookOutcome.Blocking)
                {
                    _logger?.LogInformation("SubagentStop hook blocked disposal for agent {AgentId}: {Message}",
                        context.AgentId, result.Message);

                    wasBlocked = true;
                    RecordSubagentStopHookMetrics(context.AgentType, true);
                    return SubagentStopHookResult.Block(result.Message);
                }

                if (result.PreventContinuation)
                {
                    wasBlocked = true;
                    RecordSubagentStopHookMetrics(context.AgentType, true);
                    return SubagentStopHookResult.Block(result.Message);
                }

                if (result.UpdatedInput != null)
                {
                    foreach (var kvp in result.UpdatedInput)
                    {
                        additionalData[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger?.LogWarning("SubagentStop hook timed out after {TimeoutMs}ms for agent {AgentId}, auto-proceeding",
                HookTimeout.TotalMilliseconds, context.AgentId);
        }

        RecordSubagentStopHookMetrics(context.AgentType, wasBlocked);

        return new SubagentStopHookResult { AdditionalData = additionalData };
    }

    private void RecordSubagentStopHookMetrics(string agentType, bool blocked)
        => _telemetryService?.RecordCount("hook.subagentStop.count", new() { ["agentType"] = agentType, ["blocked"] = blocked.ToString() }, description: "SubagentStop hook execution count");
}
