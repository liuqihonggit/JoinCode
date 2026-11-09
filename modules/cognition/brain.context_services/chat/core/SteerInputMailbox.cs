namespace Core.Context;

/// <summary>
/// 轮内注入邮箱实现 — 按会话隔离的 FIFO 队列（Actor 邮箱模型：投递方只入队，引擎步首 drain）。
/// 取件与投递都是 O(1) 均摊；无锁并发安全（ConcurrentDictionary + ConcurrentQueue）。
/// </summary>
[Register(typeof(ISteerInputMailbox), ServiceLifetime.Singleton)]
public sealed class SteerInputMailbox : ISteerInputMailbox {
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _queues = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Enqueue(string sessionId, string text) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (string.IsNullOrWhiteSpace(text)) {
            return;
        }
        _queues.GetOrAdd(sessionId, _ => new ConcurrentQueue<string>()).Enqueue(text);
    }

    /// <inheritdoc />
    public bool HasPending(string sessionId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _queues.TryGetValue(sessionId, out var queue) && !queue.IsEmpty;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DrainAll(string sessionId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!_queues.TryGetValue(sessionId, out var queue)) {
            return [];
        }
        _queues.TryRemove(sessionId, out _);
        var drained = new List<string>();
        while (queue.TryDequeue(out var item)) {
            drained.Add(item);
        }
        return drained;
    }
}
