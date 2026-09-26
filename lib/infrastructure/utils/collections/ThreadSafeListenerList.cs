namespace Core.Utils;

/// <summary>
/// 线程安全监听器列表 — 基于 AsyncLock 保护读写,提供注册、注销与广播通知能力
/// <para>Notify 在快照上遍历,单个监听器抛出异常不影响其他监听器</para>
/// </summary>
/// <typeparam name="T">监听器类型</typeparam>
public sealed class ThreadSafeListenerList<T> {
    private readonly List<T> _listeners = [];
    private readonly AsyncLock _lock = new("ThreadSafeListenerList");
    private readonly ILogger<ThreadSafeListenerList<T>>? _logger;

    /// <summary>
    /// 构造线程安全监听器列表
    /// </summary>
    /// <param name="logger">可选日志记录器,用于记录监听器抛出的异常</param>
    public ThreadSafeListenerList(ILogger<ThreadSafeListenerList<T>>? logger = null) {
        _logger = logger;
    }

    /// <summary>
    /// 注册监听器,返回注销令牌
    /// </summary>
    /// <param name="listener">要注册的监听器实例</param>
    /// <returns>调用 Dispose 即可注销该监听器</returns>
    public IDisposable Register(T listener) {
        ArgumentNullException.ThrowIfNull(listener);
        using (_lock.LockOrCrash()) {
            _listeners.Add(listener);
        }

        return new UnsubscribeToken(this, listener);
    }

    /// <summary>
    /// 向所有监听器广播通知
    /// </summary>
    /// <param name="action">对每个监听器执行的动作</param>
    public void Notify(Action<T> action) {
        ArgumentNullException.ThrowIfNull(action);

        T[] snapshot;
        using (_lock.LockOrCrash()) {
            snapshot = _listeners.ToArray();
        }

        foreach (var listener in snapshot) {
            try {
                action(listener);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "ThreadSafeListenerList: 监听器抛出异常");
            }
        }
    }

    /// <summary>当前已注册监听器数量</summary>
    public int Count {
        get {
            using (_lock.LockOrCrash()) {
                return _listeners.Count;
            }
        }
    }

    private void Unsubscribe(T listener) {
        using (_lock.LockOrCrash()) {
            _listeners.Remove(listener);
        }
    }

    private sealed class UnsubscribeToken(ThreadSafeListenerList<T> owner, T listener) : IDisposable {
        private int _disposed;

        /// <summary>释放资源。</summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Unsubscribe(listener);
        }
    }
}