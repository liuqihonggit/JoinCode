namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 契约变更通知路由器 — 桥接队长广播的 ContractChanged 消息到 Worker 的 ContractChangeNotifications 队列
/// </summary>
public interface IContractChangeNotificationRouter
{
    /// <summary>
    /// 获取或创建指定 agent 的通知队列
    /// </summary>
    ConcurrentQueue<string> GetOrCreateQueue(string agentId);

    /// <summary>
    /// 往目标 agent 的队列塞契约变更通知
    /// </summary>
    void EnqueueNotification(string agentId, string notification);

    /// <summary>
    /// 批量通知多个 agent
    /// </summary>
    void EnqueueNotifications(IReadOnlyList<string> agentIds, string notification);

    /// <summary>
    /// 移除指定 agent 的队列
    /// </summary>
    void RemoveQueue(string agentId);
}
