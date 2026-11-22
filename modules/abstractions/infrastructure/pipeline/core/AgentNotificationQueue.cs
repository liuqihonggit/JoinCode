namespace Infrastructure.Pipeline;

/// <summary>
/// 代理通知队列实现 — 子代理完成后入队通知，主代理或父代理出队消费
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentNotificationQueue), ServiceLifetime.Singleton)]
public sealed partial class AgentNotificationQueue : ServiceEntity, JoinCode.Abstractions.Interfaces.IAgentNotificationQueue {
    private const string MainBucketKey = "\0main";

    private readonly ConcurrentDictionary<string, ConcurrentQueue<JoinCode.Abstractions.Interfaces.QueuedNotification>> _buckets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<AgentNotificationQueue>? _logger;

    /// <summary>
    /// 构造函数 — 注入可选日志记录器
    /// </summary>
    /// <param name="logger">日志记录器，可为 null</param>
    public AgentNotificationQueue(ILogger<AgentNotificationQueue>? logger = null) {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Enqueue(string? parentAgentId, string notificationXml) {
        var notification = new JoinCode.Abstractions.Interfaces.QueuedNotification {
            Xml = notificationXml,
            TargetAgentId = parentAgentId
        };
        GetBucket(parentAgentId).Enqueue(notification);
        _logger?.LogDebug("[AgentNotificationQueue] 入队通知: TargetAgentId={AgentId}, Length={Len}",
            parentAgentId ?? "(main)", notificationXml.Length);
        Diag.WriteLine($"[NOTIFY-QUEUE] enqueue target={parentAgentId ?? "(main)"} len={notificationXml.Length}");
    }

    /// <inheritdoc/>
    public IReadOnlyList<JoinCode.Abstractions.Interfaces.QueuedNotification> DequeueAll(string? currentAgentId = null) {
        var results = new List<JoinCode.Abstractions.Interfaces.QueuedNotification>();

        if (currentAgentId is null) {
            // 主线程是顶层消费者：排空所有目标桶（保持旧行为，主代理能看到所有后台完成通知）
            foreach (var bucket in _buckets.Values)
                DrainBucket(bucket, results);
        } else {
            // 子代理只消费「广播桶 + 自己的桶」，绝不碰其他代理的桶（TASK105 D7）
            DrainExistingBucket(MainBucketKey, results);
            DrainExistingBucket(currentAgentId, results);
        }

        if (results.Count > 0) {
            _logger?.LogDebug("[AgentNotificationQueue] 出队 {Count} 条通知 (currentAgentId={AgentId})",
                results.Count, currentAgentId ?? "(main)");
        }

        return results;
    }

    /// <inheritdoc/>
    public bool HasPendingNotifications => _buckets.Values.Any(static bucket => !bucket.IsEmpty);

    private ConcurrentQueue<JoinCode.Abstractions.Interfaces.QueuedNotification> GetBucket(string? agentId)
        => _buckets.GetOrAdd(agentId ?? MainBucketKey, static _ => new ConcurrentQueue<JoinCode.Abstractions.Interfaces.QueuedNotification>());

    /// <summary>
    /// 只排空指定目标的桶，绝不遍历其他代理的桶 —— 否则会把别的代理的通知丢掉
    /// （TASK105 D7；旧实现 while TryDequeue 全队列 + 过滤即丢弃）。
    /// </summary>
    private void DrainExistingBucket(string bucketKey, List<JoinCode.Abstractions.Interfaces.QueuedNotification> results) {
        if (!_buckets.TryGetValue(bucketKey, out var bucket))
            return;

        DrainBucket(bucket, results);
    }

    private static void DrainBucket(
        ConcurrentQueue<JoinCode.Abstractions.Interfaces.QueuedNotification> bucket,
        List<JoinCode.Abstractions.Interfaces.QueuedNotification> results) {
        while (bucket.TryDequeue(out var notification))
            results.Add(notification);
    }

    /// <summary>
    /// 释放资源，清空通知队列
    /// </summary>
    public override void Dispose() {
        foreach (var bucket in _buckets.Values) {
            while (bucket.TryDequeue(out _)) { }
        }
        _buckets.Clear();
        base.Dispose();
    }
}