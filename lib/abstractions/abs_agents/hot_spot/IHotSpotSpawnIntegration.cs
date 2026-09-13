namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 热点 spawn 集成服务 — 在 Worker spawn 时注册文件写入监听器 + 获取契约变更通知队列
/// 聚合 IFileWriteListenerRegistry/IIntentCollector/IHotFileDetector/IContractChangeBroadcaster/IHotSpotTracker/IContractChangeNotificationRouter 六个依赖
/// ForkSpawnMiddleware 只需注入此一个可选参数
/// </summary>
public interface IHotSpotSpawnIntegration
{
    /// <summary>
    /// 确保 listener 已注册到 FileWriteListenerRegistry（幂等，首次调用注册，后续 no-op）
    /// IntentReportFileWriteListener: Worker 改文件自动上报意图
    /// ContractChangeBroadcastListener: 队长改热文件自动广播+塞队列
    /// </summary>
    /// <param name="captainId">队长（mainAgent）的 AgentId，用于区分队长和 Worker</param>
    void EnsureListenersRegistered(string captainId);

    /// <summary>
    /// 获取或创建 Worker 的契约变更通知队列
    /// ForkSpawnMiddleware 在 spawn 后赋值给 agent.ContractChangeNotifications
    /// </summary>
    /// <param name="agentId">Worker 的 AgentId</param>
    /// <returns>该 Worker 专属的契约变更通知队列</returns>
    ConcurrentQueue<string> GetOrCreateNotificationQueue(string agentId);
}
