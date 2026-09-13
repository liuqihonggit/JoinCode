namespace Infrastructure.Pipeline;

/// <summary>
/// 代理通知队列实现 — 子代理完成后入队通知，主代理或父代理出队消费
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentNotificationQueue), ServiceLifetime.Singleton)]
public sealed partial class AgentNotificationQueue : ServiceEntity, JoinCode.Abstractions.Interfaces.IAgentNotificationQueue
{
    private readonly ConcurrentQueue<JoinCode.Abstractions.Interfaces.QueuedNotification> _queue = new();
    private readonly ILogger<AgentNotificationQueue>? _logger;

    /// <summary>
    /// 构造函数 — 注入可选日志记录器
    /// </summary>
    /// <param name="logger">日志记录器，可为 null</param>
    public AgentNotificationQueue(ILogger<AgentNotificationQueue>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Enqueue(string? parentAgentId, string notificationXml)
    {
        var notification = new JoinCode.Abstractions.Interfaces.QueuedNotification
        {
            Xml = notificationXml,
            TargetAgentId = parentAgentId
        };
        _queue.Enqueue(notification);
        _logger?.LogDebug("[AgentNotificationQueue] 入队通知: TargetAgentId={AgentId}, Length={Len}",
            parentAgentId ?? "(main)", notificationXml.Length);
    }

    /// <inheritdoc/>
    public IReadOnlyList<JoinCode.Abstractions.Interfaces.QueuedNotification> DequeueAll(string? currentAgentId = null)
    {
        var results = new List<JoinCode.Abstractions.Interfaces.QueuedNotification>();

        while (_queue.TryDequeue(out var notification))
        {
            if (currentAgentId is null || notification.TargetAgentId is null ||
                string.Equals(notification.TargetAgentId, currentAgentId, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(notification);
            }
        }

        if (results.Count > 0)
        {
            _logger?.LogDebug("[AgentNotificationQueue] 出队 {Count} 条通知 (currentAgentId={AgentId})",
                results.Count, currentAgentId ?? "(main)");
        }

        return results;
    }

    /// <inheritdoc/>
    public bool HasPendingNotifications => !_queue.IsEmpty;
}
