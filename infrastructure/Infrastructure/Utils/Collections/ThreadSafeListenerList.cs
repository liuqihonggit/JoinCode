namespace Core.Utils;

public sealed class ThreadSafeListenerList<T>
{
    private readonly List<T> _listeners = [];
    private readonly AsyncLock _lock = new("ThreadSafeListenerList");
    private readonly ILogger<ThreadSafeListenerList<T>>? _logger;

    public ThreadSafeListenerList(ILogger<ThreadSafeListenerList<T>>? logger = null)
    {
        _logger = logger;
    }

    public IDisposable Register(T listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        using (_lock.Lock())
        {
            _listeners.Add(listener);
        }

        return new UnsubscribeToken(this, listener);
    }

    public void Notify(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        T[] snapshot;
        using (_lock.Lock())
        {
            snapshot = _listeners.ToArray();
        }

        foreach (var listener in snapshot)
        {
            try
            {
                action(listener);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "ThreadSafeListenerList: 监听器抛出异常");
            }
        }
    }

    public int Count
    {
        get
        {
            using (_lock.Lock())
            {
                return _listeners.Count;
            }
        }
    }

    private void Unsubscribe(T listener)
    {
        using (_lock.Lock())
        {
            _listeners.Remove(listener);
        }
    }

    private sealed class UnsubscribeToken(ThreadSafeListenerList<T> owner, T listener) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Unsubscribe(listener);
        }
    }
}
