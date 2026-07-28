namespace Core.Hooks.Lifecycle;

[Register]
public sealed partial class SessionStartHookManager : ISessionStartHookManager
{
    private readonly IHookOrchestrator _orchestrator;
    [Inject] private readonly ILogger<SessionStartHookManager>? _logger;
    private readonly ITelemetryService? _telemetryService;

    public SessionStartHookManager(IHookOrchestrator orchestrator, ILogger<SessionStartHookManager>? logger = null, ITelemetryService? telemetryService = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public async Task<SessionStartHookResult> OnSessionStartAsync(SessionStartHookContext context, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, JsonElement>
        {
            ["sessionId"] = JsonElementHelper.FromString(context.SessionId),
            ["source"] = JsonElementHelper.FromString(context.Source),
            ["configuration"] = JsonSerializer.SerializeToElement(context.Configuration, HooksJsonContext.Default.DictionaryStringJsonElement)
        };

        var additionalConfig = new Dictionary<string, JsonElement>();

        await foreach (var result in _orchestrator.ExecuteHooksAsync(
            HookEvent.SessionStart,
            payload,
            matcher: context.Source,
            sessionId: context.SessionId,
            cancellationToken: ct).ConfigureAwait(false))
        {
            if (result.Outcome == HookOutcome.Blocking)
            {
                _logger?.LogInformation("SessionStart hook blocked session {SessionId}: {Message}",
                    context.SessionId, result.Message);

                RecordHookMetrics(context.Source, true);
                return new SessionStartHookResult
                {
                    ShouldProceed = false,
                    Message = result.Message
                };
            }

            if (result.PreventContinuation)
            {
                return new SessionStartHookResult
                {
                    ShouldProceed = false,
                    Message = result.Message
                };
            }

            if (result.UpdatedInput != null)
            {
                foreach (var kvp in result.UpdatedInput)
                {
                    additionalConfig[kvp.Key] = kvp.Value;
                }
            }

            if (result.AdditionalContext != null)
            {
                additionalConfig["additionalContext"] = JsonElementHelper.FromString(result.AdditionalContext);
            }

            if (result.InitialUserMessage != null)
            {
                additionalConfig["initialUserMessage"] = JsonElementHelper.FromString(result.InitialUserMessage);
            }
        }

        RecordHookMetrics(context.Source, false);

        return new SessionStartHookResult
        {
            AdditionalConfig = additionalConfig
        };
    }

    private void RecordHookMetrics(string source, bool isBlocked)
        => _telemetryService?.RecordCount("hook.sessionstart.count", new() { ["source"] = source, ["blocked"] = isBlocked.ToString() }, description: "Session start hook count");
}
