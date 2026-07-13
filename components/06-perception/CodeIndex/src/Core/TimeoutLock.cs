namespace JoinCode.CodeIndex.Threading;

internal sealed class TimeoutLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly TimeSpan _defaultTimeout;
    private readonly string _lockName;
    private readonly Action<string>? _log;
    private int _disposed;

    public TimeoutLock(string lockName, TimeSpan? defaultTimeout = null, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(lockName);

        _lockName = lockName;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(5);
        _semaphore = new SemaphoreSlim(1, 1);
        _log = log;
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken ct, TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var actualTimeout = timeout ?? _defaultTimeout;
        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquiring (timeout={actualTimeout.TotalSeconds}s)...");

        var acquired = await _semaphore.WaitAsync(actualTimeout, ct).ConfigureAwait(false);
        if (!acquired)
        {
            var msg = $"[TimeoutLock:{_lockName}] TIMEOUT: failed to acquire within {actualTimeout.TotalSeconds}s. Possible deadlock detected.";
            _log?.Invoke(msg);
            System.Diagnostics.Trace.TraceError(msg);
            throw new TimeoutException($"Lock '{_lockName}' could not be acquired within {actualTimeout.TotalSeconds}s. Possible deadlock detected.");
        }

        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquired");
        return new Releaser(_lockName, _semaphore, _log);
    }

    public IDisposable Acquire(TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var actualTimeout = timeout ?? _defaultTimeout;
        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquiring sync (timeout={actualTimeout.TotalSeconds}s)...");

        var acquired = _semaphore.Wait(actualTimeout);
        if (!acquired)
        {
            var msg = $"[TimeoutLock:{_lockName}] TIMEOUT: failed to acquire within {actualTimeout.TotalSeconds}s. Possible deadlock detected.";
            _log?.Invoke(msg);
            System.Diagnostics.Trace.TraceError(msg);
            throw new TimeoutException($"Lock '{_lockName}' could not be acquired within {actualTimeout.TotalSeconds}s. Possible deadlock detected.");
        }

        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquired sync");
        return new Releaser(_lockName, _semaphore, _log);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _semaphore.Dispose();
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _sem;
        private readonly string _name;
        private readonly Action<string>? _log;
        private int _disposed;

        public Releaser(string name, SemaphoreSlim sem, Action<string>? log)
        {
            _name = name;
            _sem = sem;
            _log = log;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _sem.Release();
            _log?.Invoke($"[TimeoutLock:{_name}] Released");
        }
    }
}
