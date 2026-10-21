namespace Core.Context;

/// <summary>
/// 轮内注入邮箱实现 — 按会话隔离的 FIFO 队列（Actor 邮箱模型：投递方只入队，引擎步首 drain）。
/// 取件与投递都是 O(1) 均摊；锁只在一次入队/取件内持有（无 await，天然不可重入）。
/// </summary>
[Register(typeof(ISteerInputMailbox), ServiceLifetime.Singleton)]
public sealed class SteerInputMailbox : ISteerInputMailbox {
    private readonly Dictionary<string, Queue<string>> _queues = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <inheritdoc />
    public void Enqueue(string sessionId, string text) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (string.IsNullOrWhiteSpace(text)) {
            return;
        }
        lock (_gate) {
            if (!_queues.TryGetValue(sessionId, out var queue)) {
                queue = new Queue<string>();
                _queues[sessionId] = queue;
            }
            queue.Enqueue(text);
        }
    }

    /// <inheritdoc />
    public bool HasPending(string sessionId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_gate) {
            return _queues.TryGetValue(sessionId, out var queue) && queue.Count > 0;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DrainAll(string sessionId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_gate) {
            if (!_queues.TryGetValue(sessionId, out var queue) || queue.Count == 0) {
                return [];
            }
            var drained = new string[queue.Count];
            for (var index = 0; index < drained.Length; index++) {
                drained[index] = queue.Dequeue();
            }
            _queues.Remove(sessionId);
            return drained;
        }
    }
}
