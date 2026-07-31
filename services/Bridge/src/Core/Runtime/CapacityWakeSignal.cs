namespace Core.Bridge;

public sealed class CapacityWakeSignal : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);
    private volatile int _wakeToken;

    public void WakeUp()
    {
        Interlocked.Exchange(ref _wakeToken, Interlocked.Increment(ref _wakeToken));
        _semaphore.Release();
    }

    public async Task<bool> SleepUntilCapacityWakesAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            return await _semaphore.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
