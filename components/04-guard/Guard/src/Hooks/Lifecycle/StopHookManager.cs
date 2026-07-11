namespace Core.Hooks.Lifecycle;

public interface IStopHookManager
{
    Task<StopHookResult> OnStopAsync(StopHookContext context, CancellationToken ct = default);
}

public sealed partial class StopHookContext
{
    public required string SessionId { get; init; }
    public required string Reason { get; init; }
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

public sealed partial class StopHookResult
{
    public bool ShouldStop { get; init; } = true;
    public string? Message { get; init; }
    public Dictionary<string, JsonElement> AdditionalData { get; init; } = new();

    public static StopHookResult Continue(string? message = null) => new() { ShouldStop = false, Message = message };
    public static StopHookResult Stop(string? message = null) => new() { ShouldStop = true, Message = message };
}

[Register]
public sealed partial class StopHookManager : IStopHookManager
{
    private readonly IHookOrchestrator _orchestrator;
    [Inject] private readonly ILogger<StopHookManager>? _logger;
    private readonly ITelemetryService? _telemetryService;

    public StopHookManager(IHookOrchestrator orchestrator, ILogger<StopHookManager>? logger = null, ITelemetryService? telemetryService = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public async Task<StopHookResult> OnStopAsync(StopHookContext context, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, JsonElement>
        {
            ["sessionId"] = JsonElementHelper.FromString(context.SessionId),
            ["reason"] = JsonElementHelper.FromString(context.Reason),
            ["metadata"] = JsonSerializer.SerializeToElement(context.Metadata, HooksJsonContext.Default.DictionaryStringJsonElement)
        };

        var additionalData = new Dictionary<string, JsonElement>();

        await foreach (var result in _orchestrator.ExecuteHooksAsync(
            HookEvent.Stop,
            payload,
            sessionId: context.SessionId,
            cancellationToken: ct).ConfigureAwait(false))
        {
            if (result.Outcome == HookOutcome.Blocking)
            {
                _logger?.LogInformation("Stop hook prevented stop for session {SessionId}: {Message}",
                    context.SessionId, result.Message);

                RecordStopHookMetrics(context.Reason, true);
                return StopHookResult.Continue(result.Message);
            }

            if (result.PreventContinuation)
            {
                return StopHookResult.Continue(result.Message);
            }

            if (result.UpdatedInput != null)
            {
                foreach (var kvp in result.UpdatedInput)
                {
                    additionalData[kvp.Key] = kvp.Value;
                }
            }
        }

        RecordStopHookMetrics(context.Reason, false);

        return new StopHookResult { AdditionalData = additionalData };
    }

    private void RecordStopHookMetrics(string reason, bool prevented)
        => _telemetryService?.RecordCount("hook.stop.count", new() { ["reason"] = reason, ["prevented"] = prevented.ToString() }, description: "Stop hook execution count");
}
