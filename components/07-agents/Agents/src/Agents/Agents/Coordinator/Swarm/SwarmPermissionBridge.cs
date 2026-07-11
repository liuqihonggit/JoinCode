
namespace Core.Agents.Coordinator;

public interface ISwarmPermissionBridge : IDisposable
{
    Task SyncPermissionsAsync(string agentId, PermissionSyncRequest request, CancellationToken ct = default);

    Task<PermissionSyncState> GetPermissionStateAsync(string agentId, CancellationToken ct = default);

    Task RevokePermissionsAsync(string agentId, CancellationToken ct = default);

    event EventHandler<PermissionSyncEventArgs>? PermissionChanged;
}

public sealed partial class PermissionSyncRequest
{
    public required string AgentId { get; init; }
    public required string CoordinatorId { get; init; }
    public required PermissionMode Mode { get; init; }
    public List<string>? AllowedTools { get; init; }
    public List<string>? DeniedTools { get; init; }
    public List<string>? AllowedPaths { get; init; }
    public List<string>? DeniedPaths { get; init; }
}

public sealed partial class PermissionSyncState
{
    public required string AgentId { get; init; }
    public required PermissionMode Mode { get; init; }
    public required DateTime LastSyncedAt { get; init; }
    public IReadOnlyList<string> AllowedTools { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DeniedTools { get; init; } = Array.Empty<string>();
}

public sealed partial class PermissionSyncEventArgs : EventArgs
{
    public required string AgentId { get; init; }
    public required string ChangeType { get; init; }
    public required Dictionary<string, JsonElement> Changes { get; init; }
    public DateTime Timestamp { get; init; }
}

[Register(typeof(ISwarmPermissionBridge))]
public sealed partial class SwarmPermissionBridge : ISwarmPermissionBridge, IDisposable
{
    private readonly IAgentMessageBroker _messageBroker;
    private readonly IAgentPermissionManager _permissionManager;
    [Inject] private readonly ILogger<SwarmPermissionBridge>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;
    private readonly ConcurrentDictionary<string, PermissionSyncState> _permissionStates;
    private readonly SemaphoreSlim _lock;

    public event EventHandler<PermissionSyncEventArgs>? PermissionChanged;

    public SwarmPermissionBridge(
        IAgentMessageBroker messageBroker,
        IAgentPermissionManager permissionManager,
        ILogger<SwarmPermissionBridge>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
    {
        _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _telemetryService = telemetryService;
        _permissionStates = new ConcurrentDictionary<string, PermissionSyncState>();
        _lock = new SemaphoreSlim(1, 1);
    }

    public async Task SyncPermissionsAsync(string agentId, PermissionSyncRequest request, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rule = new AgentPermissionRule
            {
                AgentPattern = request.AgentId,
                Mode = request.Mode,
                AllowedTools = request.AllowedTools,
                DeniedTools = request.DeniedTools,
                AllowedPaths = request.AllowedPaths,
                DeniedPaths = request.DeniedPaths,
                Description = $"Synced from coordinator {request.CoordinatorId}"
            };

            await _permissionManager.AddRuleAsync(rule, ct).ConfigureAwait(false);

            var previousState = _permissionStates.GetValueOrDefault(agentId);

            var newState = new PermissionSyncState
            {
                AgentId = request.AgentId,
                Mode = request.Mode,
                LastSyncedAt = _clock.GetUtcNow(),
                AllowedTools = request.AllowedTools?.ToArray() ?? Array.Empty<string>(),
                DeniedTools = request.DeniedTools?.ToArray() ?? Array.Empty<string>()
            };

            _permissionStates[agentId] = newState;

            var changes = BuildChanges(previousState, newState);

            var message = new CoordinatorAgentMessage
            {
                FromAgentId = request.CoordinatorId,
                ToAgentId = agentId,
                MessageType = "permission_sync",
                Content = $"Permission sync: mode={request.Mode}"
            };

            await _messageBroker.SendMessageAsync(agentId, message, ct).ConfigureAwait(false);

            PermissionChanged?.Invoke(this, new PermissionSyncEventArgs
            {
                AgentId = agentId,
                ChangeType = "sync",
                Changes = changes,
                Timestamp = _clock.GetUtcNow()
            });

            _logger?.LogInformation("Synced permissions for agent {AgentId} from coordinator {CoordinatorId}, mode={Mode}",
                agentId, request.CoordinatorId, request.Mode);

            RecordPermissionBridgeMetrics("sync", true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PermissionSyncState> GetPermissionStateAsync(string agentId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_permissionStates.TryGetValue(agentId, out var state))
            {
                return state;
            }

            var rule = await _permissionManager.GetRuleForAgentAsync(agentId, ct).ConfigureAwait(false);

            return new PermissionSyncState
            {
                AgentId = agentId,
                Mode = rule?.Mode ?? PermissionMode.Auto,
                LastSyncedAt = _clock.GetUtcNow(),
                AllowedTools = rule?.AllowedTools?.ToArray() ?? Array.Empty<string>(),
                DeniedTools = rule?.DeniedTools?.ToArray() ?? Array.Empty<string>()
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RevokePermissionsAsync(string agentId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _permissionManager.RemoveRuleAsync(agentId, ct).ConfigureAwait(false);

            _permissionStates.TryRemove(agentId, out _);

            PermissionChanged?.Invoke(this, new PermissionSyncEventArgs
            {
                AgentId = agentId,
                ChangeType = "revoke",
                Changes = new Dictionary<string, JsonElement>
                {
                    ["mode"] = JsonElementHelper.FromString(PermissionMode.Deny.ToString()),
                    ["allowedTools"] = JsonElementHelper.FromJson("[]"),
                    ["deniedTools"] = JsonElementHelper.FromJson("[]")
                },
                Timestamp = _clock.GetUtcNow()
            });

            _logger?.LogInformation("Revoked permissions for agent {AgentId}", agentId);

            RecordPermissionBridgeMetrics("revoke", true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void RecordPermissionBridgeMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("permission.bridge.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Permission bridge operation count");

    private static Dictionary<string, JsonElement> BuildChanges(PermissionSyncState? previous, PermissionSyncState current)
    {
        var changes = new Dictionary<string, JsonElement>
        {
            ["mode"] = JsonElementHelper.FromString(current.Mode.ToString()),
            ["allowedTools"] = JsonElementHelper.FromString(string.Join(",", current.AllowedTools)),
            ["deniedTools"] = JsonElementHelper.FromString(string.Join(",", current.DeniedTools))
        };

        if (previous != null)
        {
            changes["previousMode"] = JsonElementHelper.FromString(previous.Mode.ToString());
        }

        return changes;
    }

    public void Dispose() => _lock.Dispose();
}
