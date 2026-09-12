namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 代理通知队列 — 对齐 TS messageQueueManager + enqueuePendingNotification
/// 后台代理完成时入队，Query 循环 drain 时消费
/// </summary>
public interface IAgentNotificationQueue
{
    /// <summary>
    /// 入队通知（后台代理完成时调用）
    /// </summary>
    /// <param name="parentAgentId">父代理 ID（null 表示主线程）</param>
    /// <param name="notificationXml">通知 XML 内容</param>
    void Enqueue(string? parentAgentId, string notificationXml);

    /// <summary>
    /// 出队所有待处理通知（Query 循环 drain 时调用）
    /// </summary>
    /// <param name="currentAgentId">当前代理 ID（null 表示主线程），只返回发给此代理的通知</param>
    IReadOnlyList<QueuedNotification> DequeueAll(string? currentAgentId = null);

    /// <summary>
    /// 是否有待处理通知
    /// </summary>
    bool HasPendingNotifications { get; }
}

/// <summary>
/// 队列中的通知条目
/// </summary>
public sealed record QueuedNotification
{
    public required string Xml { get; init; }
    public string? TargetAgentId { get; init; }
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
}
