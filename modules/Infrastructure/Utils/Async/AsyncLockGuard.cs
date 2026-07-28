namespace Core.Utils;

public readonly struct AsyncLockGuard : IDisposable
{
    private readonly SemaphoreSlim? _semaphore;

    private AsyncLockGuard(SemaphoreSlim semaphore)
    {
        _semaphore = semaphore;
    }

    public static async ValueTask<AsyncLockGuard> AcquireAsync(SemaphoreSlim semaphore, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(semaphore);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        return new AsyncLockGuard(semaphore);
    }

    public static AsyncLockGuard Acquire(SemaphoreSlim semaphore)
    {
        ArgumentNullException.ThrowIfNull(semaphore);
        semaphore.Wait();
        return new AsyncLockGuard(semaphore);
    }

    public void Dispose() => _semaphore?.Release();
}
