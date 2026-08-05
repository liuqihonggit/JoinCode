namespace Core.Utils;

public sealed class ThreadSafeListenerList<T>
{
    private readonly List<T> _listeners = [];
    private readonly object _lock = new();
    private readonly ILogger<ThreadSafeListenerList<T>>? _logger;

    public ThreadSafeListenerList(ILogger<ThreadSafeListenerList<T>>? logger = null)
    {
        _logger = logger;
    }

    public IDisposable Register(T listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_lock)
        {
            _listeners.Add(listener);
        }

        return new UnsubscribeToken(this, listener);
    }

    public void Notify(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        T[] snapshot;
        lock (_lock)
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

    private void Unsubscribe(T listener)
    {
        lock (_lock)
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
