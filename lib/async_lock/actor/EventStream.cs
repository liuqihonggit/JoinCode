namespace Core.Utils;

/// <summary>
/// 全局事件总线 — 发布/订阅模式,所有 Actor 共享(Akka EventStream 对齐)。
/// <para>Subscribe&lt;T&gt; 订阅类型 T 的事件,Publish&lt;T&gt; 发布事件通知所有订阅者。</para>
/// <para>订阅者异常隔离:一个订阅者抛异常不影响其他订阅者(记日志继续)。</para>
/// <para>线程安全:ConcurrentDictionary + lock 保证订阅/取消/发布的原子性。</para>
/// </summary>
public sealed class EventStream {
    private readonly ConcurrentDictionary<Type, Delegate?> _subscribers = new();
    private readonly object _lock = new();

    /// <summary>
    /// 订阅类型 T 的事件 — 返回 IDisposable 用于取消订阅(using 模式)。
    /// <para>同一 handler 可多次订阅(多次调用),Publish 时多次通知。</para>
    /// </summary>
    /// <typeparam name="T">事件类型</typeparam>
    /// <param name="handler">事件处理委托</param>
    /// <returns>订阅句柄,Dispose 取消订阅</returns>
    public IDisposable Subscribe<T>(Action<T> handler) {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock) {
            _subscribers.AddOrUpdate(typeof(T), handler, (_, existing) => Delegate.Combine(existing, handler));
        }
        return new Subscription<T>(this, handler);
    }

    /// <summary>
    /// 发布类型 T 的事件 — 通知所有订阅者,异常隔离(一个订阅者异常不影响其他)。
    /// </summary>
    /// <typeparam name="T">事件类型</typeparam>
    /// <param name="evt">事件实例</param>
    public void Publish<T>(T evt) {
        if (!_subscribers.TryGetValue(typeof(T), out var del) || del is null) return;
        foreach (var h in del.GetInvocationList()) {
            try {
                ((Action<T>)h)(evt);
            } catch (Exception ex) {
                AsyncStderrWriter.Enqueue($"[EventStream] 订阅者异常忽略: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 取消订阅 — 移除指定 handler。
    /// </summary>
    /// <typeparam name="T">事件类型</typeparam>
    /// <param name="handler">要移除的委托</param>
    public void Unsubscribe<T>(Action<T> handler) {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock) {
            _subscribers.AddOrUpdate(typeof(T), (Delegate?)null, (_, existing) => Delegate.Remove(existing, handler));
        }
    }

    private sealed class Subscription<T>(EventStream stream, Action<T> handler) : IDisposable {
        private int _disposed;
        /// <summary>取消订阅 — 移除 handler,幂等(多次调用安全)</summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                stream.Unsubscribe(handler);
            }
        }
    }
}
