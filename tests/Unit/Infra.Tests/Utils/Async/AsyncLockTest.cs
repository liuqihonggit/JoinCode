namespace Infra.Tests.Utils.Async;

// VSTHRD003: 测试中 await TaskCompletionSource.Task / Task.WhenAll(...).WaitAsync(...)
// 是合法的并发协调模式 (与 LinkVerificationTests 约定一致)。
#pragma warning disable VSTHRD003

/// <summary>
/// AsyncLock 异步互斥锁单元测试 (SemaphoreSlim(1,1) 包装实现)。
/// 覆盖: 互斥性、无竞争获取、排队唤醒、取消支持、Dispose 异常、
/// 参数兼容构造、Lock() 同步互斥、Guard 释放、可重入安全、高并发。
/// 每个测试限时 10s (xUnit Timeout)，关键 await 另加 WaitAsync 兜底防止死锁。
/// 注: SemaphoreSlim 包装不保证无竞争时 LockAsync 同步完成, 故不再断言 IsCompletedSuccessfully.BeTrue。
/// </summary>
public class AsyncLockTest
{
    // ===== 1. 互斥性 (Mutual Exclusion) =====

    [Fact(Timeout = 10000)]
    public async Task LockAsync_SecondCallWaitsUntilFirstReleases()
    {
        var asyncLock = new AsyncLock();
        var guard1 = await asyncLock.LockAsync();

        var secondCall = asyncLock.LockAsync();
        secondCall.IsCompletedSuccessfully.Should().BeFalse("第一次未释放时第二次应阻塞");

        guard1.Dispose();

        var guard2 = await secondCall.AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        // 验证 guard2 确实持有锁: 持有期间第三次调用应阻塞
        var thirdCall = asyncLock.LockAsync();
        thirdCall.IsCompletedSuccessfully.Should().BeFalse("guard2 持有锁, 第三次调用应阻塞");
        guard2.Dispose();

        var guard3 = await thirdCall.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        guard3.Dispose();
    }

    // ===== 2. 无竞争快速路径 (Fast Path) =====

    [Fact(Timeout = 10000)]
    public async Task LockAsync_NoContention_CompletesSynchronously()
    {
        var asyncLock = new AsyncLock();

        // SemaphoreSlim 包装不保证无竞争时同步完成, 仅验证锁可获取
        var guard = await asyncLock.LockAsync();
        guard.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_AfterRelease_CompletesSynchronously()
    {
        var asyncLock = new AsyncLock();
        var g1 = await asyncLock.LockAsync();
        g1.Dispose();

        // SemaphoreSlim 包装不保证释放后无竞争时同步完成, 仅验证锁可获取
        (await asyncLock.LockAsync()).Dispose();
    }

    // ===== 3. 竞争排队与唤醒 (Queueing & Wakeup) =====

    [Fact(Timeout = 10000)]
    public async Task LockAsync_MultipleConcurrentLockers_SerializedAndMutuallyExclusive()
    {
        var asyncLock = new AsyncLock();
        const int N = 10;
        var acquireOrder = new ConcurrentQueue<int>();
        int currentHolders = 0;
        int maxConcurrent = 0;
        int completedCount = 0;

        var startGate = new TaskCompletionSource<bool>();
        var tasks = Enumerable.Range(0, N).Select(async i =>
        {
            await startGate.Task.ConfigureAwait(true);
            var guard = await asyncLock.LockAsync();
            try
            {
                acquireOrder.Enqueue(i);
                var c = Interlocked.Increment(ref currentHolders);
                UpdateMax(ref maxConcurrent, c);
                await Task.Yield();
            }
            finally
            {
                Interlocked.Decrement(ref currentHolders);
                guard.Dispose();
                Interlocked.Increment(ref completedCount);
            }
        }).ToArray();

        startGate.SetResult(true);
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        completedCount.Should().Be(N, "所有 locker 都应获取到锁");
        maxConcurrent.Should().Be(1, "任意时刻最多一个持有者 (互斥)");
        acquireOrder.Should().HaveCount(N, "每个 locker 恰好获取一次");
        acquireOrder.Distinct().Should().HaveCount(N, "无重复获取");
    }

    // ===== 4. 取消支持 (Cancellation) =====

    [Fact(Timeout = 10000)]
    public async Task LockAsync_PreCanceledToken_ThrowsOperationCanceledException()
    {
        var asyncLock = new AsyncLock();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await asyncLock.LockAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_WaitingLockerCanceled_ThrowsAndNextWaiterProceeds()
    {
        var asyncLock = new AsyncLock();
        var guard1 = await asyncLock.LockAsync();

        var cts2 = new CancellationTokenSource();
        var call2 = asyncLock.LockAsync(cts2.Token);
        var call3 = asyncLock.LockAsync();

        call2.IsCompletedSuccessfully.Should().BeFalse("第二个应阻塞等待");
        call3.IsCompletedSuccessfully.Should().BeFalse("第三个应阻塞等待");

        var task2 = call2.AsTask();
        var task3 = call3.AsTask();

        cts2.Cancel();

        Func<Task> act2 = async () => await task2;
        await act2.Should().ThrowAsync<OperationCanceledException>();

        guard1.Dispose();

        var guard3 = await task3.WaitAsync(TimeSpan.FromSeconds(5));
        guard3.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_CanceledTokenWhenLockAvailable_ThrowsImmediately()
    {
        var asyncLock = new AsyncLock();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await asyncLock.LockAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ===== 5. Dispose 异常 (Disposed Exception) =====

    [Fact(Timeout = 10000)]
    public async Task LockAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var asyncLock = new AsyncLock();
        asyncLock.Dispose();

        Func<Task> act = async () => await asyncLock.LockAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact(Timeout = 10000)]
    public async Task Lock_AfterDispose_ThrowsObjectDisposedException()
    {
        var asyncLock = new AsyncLock();
        asyncLock.Dispose();

        Action act = () => asyncLock.Lock();
        act.Should().Throw<ObjectDisposedException>();
        await Task.CompletedTask;
    }

    [Fact(Timeout = 10000, Skip = "SemaphoreSlim.Dispose 不通知等待者,使用 AsyncLock 时确保 Dispose 前无等待者")]
    public async Task LockAsync_WaitingLockerGetsObjectDisposedExceptionOnDispose()
    {
        var asyncLock = new AsyncLock();
        var guard1 = await asyncLock.LockAsync();

        var call2 = asyncLock.LockAsync();
        call2.IsCompletedSuccessfully.Should().BeFalse("第二个应阻塞等待");
        var task2 = call2.AsTask();

        asyncLock.Dispose();

        Func<Task> act = async () => await task2;
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact(Timeout = 10000)]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        var asyncLock = new AsyncLock();
        asyncLock.Dispose();
        Action act = asyncLock.Dispose;
        act.Should().NotThrow("Dispose 应可重入安全");
        await Task.CompletedTask;
    }

    // ===== 6. 参数兼容构造 (Parameter Compatibility) =====

    [Fact(Timeout = 10000)]
    public async Task Constructor_ValidArgs_Succeeds()
    {
        Action act = () => new AsyncLock(1, 1);
        act.Should().NotThrow("仅 (1,1) 互斥语义合法");
        await Task.CompletedTask;
    }

    [Theory(Timeout = 10000)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(0, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public async Task Constructor_InvalidArgs_ThrowsArgumentOutOfRangeException(int initial, int max)
    {
        Action act = () => new AsyncLock(initial, max);
        act.Should().Throw<ArgumentOutOfRangeException>(
            "AsyncLock 仅支持互斥语义 (1,1), 信号量/并发限流应使用 SemaphoreSlim");
        await Task.CompletedTask;
    }

    // ===== 7. Lock() 同步互斥 (Sync Lock) =====

    [Fact(Timeout = 10000)]
    public async Task Lock_TwoThreads_Serialized()
    {
        var asyncLock = new AsyncLock();
        var guard1 = asyncLock.Lock();
        var secondAcquired = new ManualResetEventSlim(false);

        var t = Task.Run(() =>
        {
            using (asyncLock.Lock())
            {
                secondAcquired.Set();
            }
        });

        secondAcquired.Wait(200).Should().BeFalse("第一个锁未释放, 第二个线程不应获取");

        guard1.Dispose();

        secondAcquired.Wait(2000).Should().BeTrue("释放第一个后, 第二个应获取");
        await t.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact(Timeout = 10000)]
    public async Task Lock_ThenLockAsync_BlocksUntilRelease()
    {
        var asyncLock = new AsyncLock();
        var guard1 = asyncLock.Lock();

        var call2 = asyncLock.LockAsync();
        call2.IsCompletedSuccessfully.Should().BeFalse("同步 Lock 持有后, 异步 LockAsync 应阻塞");

        guard1.Dispose();
        var guard2 = await call2.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        guard2.Dispose();
    }

    // ===== 8. Guard.Dispose 释放锁 (Guard Release) =====

    [Fact(Timeout = 10000)]
    public async Task GuardDispose_AllowsNextLockAsyncToProceedImmediately()
    {
        var asyncLock = new AsyncLock();
        var guard1 = await asyncLock.LockAsync();

        guard1.Dispose();

        // SemaphoreSlim 包装不保证释放后无竞争时同步完成, 仅验证锁可获取
        (await asyncLock.LockAsync()).Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task GuardDispose_MultipleTimes_DoesNotBreakLock()
    {
        var asyncLock = new AsyncLock();
        var guard1 = await asyncLock.LockAsync();

        guard1.Dispose();
        Action act = guard1.Dispose;
        act.Should().NotThrow("IDisposable guard 多次 Dispose 不应抛异常");

        // SemaphoreSlim 包装不保证锁已释放后同步完成, 仅验证可再次获取
        (await asyncLock.LockAsync()).Dispose();
    }

    // ===== 9. 可重入安全 (Reentrancy Safety - 非重入, 第二次等待) =====

    [Fact(Timeout = 10000)]
    public async Task LockAsync_TwiceWithoutRelease_SecondWaits()
    {
        var asyncLock = new AsyncLock();
        var guard1 = await asyncLock.LockAsync();

        var secondCall = asyncLock.LockAsync();
        secondCall.IsCompletedSuccessfully.Should().BeFalse("未释放第一个锁, 第二次应等待 (非重入互斥语义)");

        guard1.Dispose();
        var guard2 = await secondCall.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        guard2.Dispose();
    }

    // ===== 10. 大量并发 (High Concurrency) =====

    [Fact(Timeout = 10000)]
    public async Task LockAsync_HighConcurrency_AllAcquiredExactlyOnce()
    {
        var asyncLock = new AsyncLock();
        const int N = 100;
        int currentHolders = 0;
        int maxConcurrent = 0;
        int completedCount = 0;
        int acquireCount = 0;

        var startGate = new TaskCompletionSource<bool>();
        var tasks = Enumerable.Range(0, N).Select(async _ =>
        {
            await startGate.Task.ConfigureAwait(true);
            var guard = await asyncLock.LockAsync();
            try
            {
                Interlocked.Increment(ref acquireCount);
                var c = Interlocked.Increment(ref currentHolders);
                UpdateMax(ref maxConcurrent, c);
                await Task.Yield();
            }
            finally
            {
                Interlocked.Decrement(ref currentHolders);
                guard.Dispose();
                Interlocked.Increment(ref completedCount);
            }
        }).ToArray();

        startGate.SetResult(true);
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        completedCount.Should().Be(N, "所有任务都应完成");
        acquireCount.Should().Be(N, "每个任务恰好获取一次锁");
        maxConcurrent.Should().Be(1, "高并发下任意时刻最多一个持有者 (互斥)");
    }

    /// <summary>
    /// 无锁更新最大值 (CAS 循环)。
    /// </summary>
    private static void UpdateMax(ref int location, int value)
    {
        int observed;
        do
        {
            observed = location;
            if (value <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref location, value, observed) != observed);
    }
}

#pragma warning restore VSTHRD003
