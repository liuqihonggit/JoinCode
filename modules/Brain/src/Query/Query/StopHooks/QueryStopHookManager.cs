namespace Core.Query.StopHooks;

public interface IQueryStopHookManager
{
    Task<StopHookResult> ExecuteStopHooksAsync(string sessionId, string reason, CancellationToken ct = default);
    void RegisterStopHook(IQueryStopHook hook);
    void UnregisterStopHook(string hookName);
}

public interface IQueryStopHook
{
    string Name { get; }
    int Priority { get; }
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
public sealed partial class QueryStopHookManager : IQueryStopHookManager
{
    private readonly ConcurrentDictionary<string, IQueryStopHook> _hooks;
    [Inject] private readonly ILogger<QueryStopHookManager>? _logger;
    private readonly ITelemetryService? _telemetryService;

    public QueryStopHookManager(ILogger<QueryStopHookManager>? logger = null, ITelemetryService? telemetryService = null)
    {
        _hooks = new ConcurrentDictionary<string, IQueryStopHook>(StringComparer.Ordinal);
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public async Task<StopHookResult> ExecuteStopHooksAsync(string sessionId, string reason, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(reason);

        var context = new StopHookContext
        {
            SessionId = sessionId,
            Reason = reason
        };

        var sortedHooks = _hooks.Values.OrderBy(h => h.Priority).ToList();

        foreach (var hook in sortedHooks)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await hook.OnStopAsync(context, ct).ConfigureAwait(false);
                if (result.ShouldStop)
                {
                    _logger?.LogInformation("[QueryStopHookManager] Hook '{HookName}' requested stop: {Message}", hook.Name, result.Message);
                    RecordStopHookMetrics(hook.Name, true);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[QueryStopHookManager] Hook '{HookName}' threw exception", hook.Name);
                RecordStopHookMetrics(hook.Name, false);
            }
        }

        return StopHookResult.Stop();
    }

    public void RegisterStopHook(IQueryStopHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hooks[hook.Name] = hook;
        _logger?.LogDebug("[QueryStopHookManager] Registered stop hook: {HookName} (Priority: {Priority})", hook.Name, hook.Priority);
    }

    public void UnregisterStopHook(string hookName)
    {
        ArgumentNullException.ThrowIfNull(hookName);
        _hooks.TryRemove(hookName, out _);
        _logger?.LogDebug("[QueryStopHookManager] Unregistered stop hook: {HookName}", hookName);
    }

    private void RecordStopHookMetrics(string hookName, bool isSuccess)
        => _telemetryService?.RecordCount("query.stophook.count", new() { ["hook"] = hookName, ["success"] = isSuccess.ToString() }, "count", "Query stop hook execution count");
}
