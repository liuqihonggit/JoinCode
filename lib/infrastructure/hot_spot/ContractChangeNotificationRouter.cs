namespace Infrastructure.HotSpot;

/// <summary>
/// 契约变更通知路由器 — 维护 agentId → ConcurrentQueue 映射
/// 队长广播 ContractChanged 时，往目标 Worker 的队列塞通知
/// Worker 的 AgentBase.ContractChangeNotifications 指向此路由器管理的队列
/// </summary>
[Register(typeof(IContractChangeNotificationRouter), ServiceLifetime.Singleton)]
public sealed class ContractChangeNotificationRouter : IContractChangeNotificationRouter {
    private ImmutableDictionary<string, ConcurrentQueue<string>> _queues = ImmutableDictionary<string, ConcurrentQueue<string>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ContractChangeNotificationRouter>? _logger;

    /// <summary>
    /// 构造函数 — 注入可选日志记录器
    /// </summary>
    /// <param name="logger">日志记录器，可为 null</param>
    public ContractChangeNotificationRouter(ILogger<ContractChangeNotificationRouter>? logger = null) {
        _logger = logger;
    }

    /// <summary>
    /// 获取或创建指定 agent 的通知队列（Worker spawn 时调用，赋给 AgentBase.ContractChangeNotifications）
    /// </summary>
    public ConcurrentQueue<string> GetOrCreateQueue(string agentId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return GetOrAddQueue(agentId);
    }

    /// <summary>
    /// 往目标 agent 的队列塞契约变更通知（队长广播时调用）
    /// </summary>
    public void EnqueueNotification(string agentId, string notification) {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notification);

        var queue = GetOrAddQueue(agentId);
        queue.Enqueue(notification);
        _logger?.LogDebug("[ContractRoute] 通知已塞入 {AgentId} 的队列", agentId);
    }

    /// <summary>
    /// 批量通知多个 agent
    /// </summary>
    public void EnqueueNotifications(IReadOnlyList<string> agentIds, string notification) {
        ArgumentNullException.ThrowIfNull(agentIds);
        foreach (var agentId in agentIds) {
            EnqueueNotification(agentId, notification);
        }
    }

    /// <summary>
    /// 移除指定 agent 的队列（Worker 结束时调用）
    /// </summary>
    public void RemoveQueue(string agentId) {
        ImmutableInterlocked.Update(ref _queues, d => d.Remove(agentId));
    }

    private ConcurrentQueue<string> GetOrAddQueue(string agentId) {
        var snapshot = Volatile.Read(ref _queues);
        if (snapshot.TryGetValue(agentId, out var existing))
            return existing;

        var newQueue = new ConcurrentQueue<string>();
        ImmutableInterlocked.Update(ref _queues, d => d.ContainsKey(agentId) ? d : d.Add(agentId, newQueue));
        return Volatile.Read(ref _queues)[agentId];
    }
}