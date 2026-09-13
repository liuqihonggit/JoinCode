
namespace Core.Agents.Coordinator;

/// <summary>
/// Swarm 权限同步桥接口 — 在 Leader 与 Worker 之间同步权限状态
/// </summary>
public interface ISwarmPermissionBridge : IDisposable
{
    /// <summary>
    /// 异步同步指定智能体的权限配置
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="request">权限同步请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task SyncPermissionsAsync(string agentId, PermissionSyncRequest request, CancellationToken ct = default);

    /// <summary>
    /// 异步获取指定智能体的权限同步状态
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>权限同步状态</returns>
    Task<PermissionSyncState> GetPermissionStateAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// 异步撤销指定智能体的所有权限
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task RevokePermissionsAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// 权限变更事件
    /// </summary>
    event EventHandler<PermissionSyncEventArgs>? PermissionChanged;
}

/// <summary>
/// 权限同步请求 — 从协调器同步权限到指定智能体
/// </summary>
public sealed partial class PermissionSyncRequest
{
    /// <summary>
    /// 目标智能体标识
    /// </summary>
    public required string AgentId { get; init; }
    /// <summary>
    /// 协调器智能体标识
    /// </summary>
    public required string CoordinatorId { get; init; }
    /// <summary>
    /// 权限模式
    /// </summary>
    public required PermissionMode Mode { get; init; }
    /// <summary>
    /// 允许的工具列表
    /// </summary>
    public List<string> AllowedTools { get; init; } = [];
    /// <summary>
    /// 拒绝的工具列表
    /// </summary>
    public List<string> DeniedTools { get; init; } = [];
    /// <summary>
    /// 允许的路径列表
    /// </summary>
    public List<string> AllowedPaths { get; init; } = [];
    /// <summary>
    /// 拒绝的路径列表
    /// </summary>
    public List<string> DeniedPaths { get; init; } = [];
}

/// <summary>
/// 权限同步状态 — 智能体当前权限的快照
/// </summary>
public sealed partial class PermissionSyncState
{
    /// <summary>
    /// 智能体标识
    /// </summary>
    public required string AgentId { get; init; }
    /// <summary>
    /// 权限模式
    /// </summary>
    public required PermissionMode Mode { get; init; }
    /// <summary>
    /// 最近一次同步时间
    /// </summary>
    public required DateTime LastSyncedAt { get; init; }
    /// <summary>
    /// 允许的工具只读列表
    /// </summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = Array.Empty<string>();
    /// <summary>
    /// 拒绝的工具只读列表
    /// </summary>
    public IReadOnlyList<string> DeniedTools { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 权限同步事件参数 — 权限变更时携带的上下文信息
/// </summary>
public sealed partial class PermissionSyncEventArgs : EventArgs
{
    /// <summary>
    /// 智能体标识
    /// </summary>
    public required string AgentId { get; init; }
    /// <summary>
    /// 变更类型（sync/revoke）
    /// </summary>
    public required string ChangeType { get; init; }
    /// <summary>
    /// 变更内容字典
    /// </summary>
    public required Dictionary<string, JsonElement> Changes { get; init; }
    /// <summary>
    /// 事件时间戳
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>Swarm 权限桥 — 在 Swarm 协调层与权限管理器之间同步权限状态，处理权限变更事件与跨代理权限传播</summary>
[Register(typeof(ISwarmPermissionBridge), ServiceLifetime.Singleton)]
public sealed partial class SwarmPermissionBridge : ServiceEntity, ISwarmPermissionBridge, IDisposable
{
    private readonly IMailbox _messageBroker;
    private readonly IAgentPermissionManager _permissionManager;
    private readonly ILogger<SwarmPermissionBridge>? _logger;
    private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;
    private readonly ConcurrentDictionary<string, PermissionSyncState> _permissionStates;
    private readonly AsyncLock _lock = new();

    /// <summary>
    /// 权限变更事件
    /// </summary>
    public event EventHandler<PermissionSyncEventArgs>? PermissionChanged;

    /// <summary>
    /// 初始化 Swarm 权限同步桥
    /// </summary>
    /// <param name="messageBroker">消息邮箱</param>
    /// <param name="permissionManager">权限管理器</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="telemetryService">遥测服务</param>
    /// <param name="clock">时钟服务</param>
    public SwarmPermissionBridge(
        IMailbox messageBroker,
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
    }

    /// <summary>
    /// 异步同步指定智能体的权限配置，更新规则并触发变更事件
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="request">权限同步请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task SyncPermissionsAsync(string agentId, PermissionSyncRequest request, CancellationToken ct = default)
    {
                using (await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
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

            await _messageBroker.SendAsync(agentId, message, ct).ConfigureAwait(false);

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
    }

    /// <summary>
    /// 异步获取指定智能体的权限同步状态
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>权限同步状态</returns>
    public async Task<PermissionSyncState> GetPermissionStateAsync(string agentId, CancellationToken ct = default)
    {
                using (await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
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
    }

    /// <summary>
    /// 异步撤销指定智能体的所有权限并触发 revoke 事件
    /// </summary>
    /// <param name="agentId">智能体标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task RevokePermissionsAsync(string agentId, CancellationToken ct = default)
    {
                using (await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            await _permissionManager.RemoveRuleAsync(agentId, ct).ConfigureAwait(false);

            _permissionStates.TryRemove(agentId, out _);

            PermissionChanged?.Invoke(this, new PermissionSyncEventArgs
            {
                AgentId = agentId,
                ChangeType = "revoke",
                Changes = new Dictionary<string, JsonElement>
                {
                    ["mode"] = JsonElementHelper.FromString(PermissionMode.Ask.ToString()),
                    ["allowedTools"] = JsonElementHelper.FromJson("[]"),
                    ["deniedTools"] = JsonElementHelper.FromJson("[]")
                },
                Timestamp = _clock.GetUtcNow()
            });

            _logger?.LogInformation("Revoked permissions for agent {AgentId}", agentId);

            RecordPermissionBridgeMetrics("revoke", true);
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

    /// <summary>释放资源 — 释放权限同步锁</summary>
    protected override void OnDispose() => _lock.Dispose();
}
