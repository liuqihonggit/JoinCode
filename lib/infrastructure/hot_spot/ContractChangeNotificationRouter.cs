namespace Infrastructure.HotSpot;

/// <summary>
/// 契约变更通知路由器 — 维护 agentId → ConcurrentQueue 映射
/// 队长广播 ContractChanged 时，往目标 Worker 的队列塞通知
/// Worker 的 AgentBase.ContractChangeNotifications 指向此路由器管理的队列
/// </summary>
[Register(typeof(IContractChangeNotificationRouter), ServiceLifetime.Singleton)]
public sealed class ContractChangeNotificationRouter : IContractChangeNotificationRouter
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ContractChangeNotificationRouter>? _logger;

    public ContractChangeNotificationRouter(ILogger<ContractChangeNotificationRouter>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取或创建指定 agent 的通知队列（Worker spawn 时调用，赋给 AgentBase.ContractChangeNotifications）
    /// </summary>
    public ConcurrentQueue<string> GetOrCreateQueue(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return _queues.GetOrAdd(agentId, _ => new ConcurrentQueue<string>());
    }

    /// <summary>
    /// 往目标 agent 的队列塞契约变更通知（队长广播时调用）
    /// </summary>
    public void EnqueueNotification(string agentId, string notification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notification);

        var queue = _queues.GetOrAdd(agentId, _ => new ConcurrentQueue<string>());
        queue.Enqueue(notification);
        _logger?.LogDebug("[ContractRoute] 通知已塞入 {AgentId} 的队列", agentId);
    }

    /// <summary>
    /// 批量通知多个 agent
    /// </summary>
    public void EnqueueNotifications(IReadOnlyList<string> agentIds, string notification)
    {
        ArgumentNullException.ThrowIfNull(agentIds);
        foreach (var agentId in agentIds)
        {
            EnqueueNotification(agentId, notification);
        }
    }

    /// <summary>
    /// 移除指定 agent 的队列（Worker 结束时调用）
    /// </summary>
    public void RemoveQueue(string agentId)
    {
        _queues.TryRemove(agentId, out _);
    }
}
