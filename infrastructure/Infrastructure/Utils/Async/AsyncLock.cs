namespace Core.Utils;

public sealed class AsyncLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public ValueTask<AsyncLockGuard> LockAsync(CancellationToken ct = default)
        => AsyncLockGuard.AcquireAsync(_semaphore, ct);

    public AsyncLockGuard Lock()
        => AsyncLockGuard.Acquire(_semaphore);

    public void Dispose() => _semaphore.Dispose();
}
