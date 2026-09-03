namespace AsyncLockBenchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class ContentionBench
{
    private AsyncLock _asyncLock = null!;
    private SemaphoreSlim _semaphore = null!;

    [Params(4, 8)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _asyncLock = new AsyncLock();
        _semaphore = new SemaphoreSlim(1, 1);
    }

    [Benchmark(Description = "AsyncLock 竞争(async)")]
    public async Task AsyncLock_Contention_Async()
    {
        var tasks = new Task[Concurrency];
        for (var i = 0; i < Concurrency; i++)
            tasks[i] = Task.Run(async () => { using var g = _asyncLock.TryLock() ?? throw new System.TimeoutException("锁等待超时"); });
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "SemaphoreSlim 竞争(async)")]
    public async Task SemaphoreSlim_Contention_Async()
    {
        var tasks = new Task[Concurrency];
        for (var i = 0; i < Concurrency; i++)
            tasks[i] = Task.Run(async () => { await _semaphore.WaitAsync(); _semaphore.Release(); });
        await Task.WhenAll(tasks);
    }
}
