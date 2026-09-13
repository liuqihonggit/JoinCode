namespace JoinCode.CodeIndex.Threading;

internal sealed class TimeoutLock : IDisposable
{
    private readonly AsyncLock _semaphore = new();
    private readonly TimeSpan _defaultTimeout;
    private readonly string _lockName;
    private readonly Action<string>? _log;
    private int _disposed;

    public TimeoutLock(string lockName, TimeSpan? defaultTimeout = null, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(lockName);

        _lockName = lockName;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(5);

        _log = log;
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken ct, TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var actualTimeout = timeout ?? _defaultTimeout;
        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquiring (timeout={actualTimeout.TotalSeconds}s)...");

        var guard = _semaphore.TryLock();
        if (guard is null)
        {
            var msg = $"[TimeoutLock:{_lockName}] TIMEOUT: failed to acquire within {actualTimeout.TotalSeconds}s. Possible deadlock detected.";
            _log?.Invoke(msg);
            System.Diagnostics.Trace.TraceError(msg);
            throw new TimeoutException($"Lock '{_lockName}' could not be acquired within {actualTimeout.TotalSeconds}s. Possible deadlock detected.");
        }

        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquired");
        return new Releaser(_lockName, guard, _log);
    }

    public IDisposable Acquire(TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var actualTimeout = timeout ?? _defaultTimeout;
        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquiring sync (timeout={actualTimeout.TotalSeconds}s)...");

        var guard = _semaphore.TryLock();
        if (guard is null)
        {
            var msg = $"[TimeoutLock:{_lockName}] TIMEOUT: failed to acquire within {actualTimeout.TotalSeconds}s. Possible deadlock detected.";
            _log?.Invoke(msg);
            System.Diagnostics.Trace.TraceError(msg);
            throw new TimeoutException($"Lock '{_lockName}' could not be acquired within {actualTimeout.TotalSeconds}s. Possible deadlock detected.");
        }

        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquired sync");
        return new Releaser(_lockName, guard, _log);
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
        _semaphore.Dispose();
    }

    private sealed class Releaser : IDisposable
    {
        private readonly IDisposable _guard;
        private readonly string _name;
        private readonly Action<string>? _log;
        private int _disposed;

        public Releaser(string name, IDisposable guard, Action<string>? log)
        {
            _name = name;
            _guard = guard;
            _log = log;
        }

        public void Dispose()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            _guard.Dispose();
            _log?.Invoke($"[TimeoutLock:{_name}] Released");
        }
    }
}
