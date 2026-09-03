namespace AsyncLockBenchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class NoContentionBench
{
    private AsyncLock _asyncLock = null!;
    private SemaphoreSlim _semaphore = null!;

    [GlobalSetup]
    public void Setup()
    {
        _asyncLock = new AsyncLock(nameof(NoContentionBench));
        _semaphore = new SemaphoreSlim(1, 1);
    }

    [Benchmark(Description = "AsyncLock 无竞争(async)")]
    public async Task AsyncLock_NoContention_Async()
    {
        using var guard = _asyncLock.TryLock() ?? throw new System.TimeoutException("锁等待超时");
    }

    [Benchmark(Description = "SemaphoreSlim 无竞争(async)")]
    public async Task SemaphoreSlim_NoContention_Async()
    {
        await _semaphore.WaitAsync();
        _semaphore.Release();
    }

    [Benchmark(Description = "AsyncLock 无竞争(sync)")]
    public void AsyncLock_NoContention_Sync()
    {
        using var guard = _asyncLock.TryLock() ?? throw new System.TimeoutException("锁等待超时");
    }

    [Benchmark(Description = "SemaphoreSlim 无竞争(sync)")]
    public void SemaphoreSlim_NoContention_Sync()
    {
        _semaphore.Wait();
        _semaphore.Release();
    }
}
