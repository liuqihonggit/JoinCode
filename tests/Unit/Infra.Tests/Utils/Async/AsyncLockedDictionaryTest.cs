namespace Infra.Tests.Utils.Async;

#pragma warning disable VSTHRD003

/// <summary>
/// AsyncLockedDictionary 单元测试。
/// 内部实现从 Dictionary+AsyncLock(单全局锁) 切换为 ConcurrentDictionary(分片锁)后,
/// 核心不变量:不同 key 的异步 factory 可并行执行(分片锁语义),同 key 仍串行互斥。
/// 每个测试限时 10s,关键 await 加 WaitAsync 兜底防死锁。
/// </summary>
public class AsyncLockedDictionaryTest
{
    // ===== 1. 基本功能 (Basic Functionality) =====

    [Fact(Timeout = 10000)]
    public async Task GetOrAddAsync_SyncFactory_NewKey_AddsAndReturns()
    {
        var dict = new AsyncLockedDictionary<string, int>();
        var v = await dict.GetOrAddAsync("a", _ => 42);
        v.Should().Be(42);

        var v2 = await dict.GetOrAddAsync("a", _ => 999);
        v2.Should().Be(42, "已存在应返回原值, 不调用 factory");
    }

    [Fact(Timeout = 10000)]
    public async Task GetOrAddAsync_AsyncFactory_NewKey_AddsAndReturns()
    {
        var dict = new AsyncLockedDictionary<string, int>();
        var v = await dict.GetOrAddAsync("a", _ => Task.FromResult(42));
        v.Should().Be(42);

        var v2 = await dict.GetOrAddAsync("a", _ => Task.FromResult(999));
        v2.Should().Be(42);
    }

    [Fact(Timeout = 10000)]
    public async Task TryAddAsync_NewKey_ReturnsTrue_DuplicateReturnsFalse()
    {
        var dict = new AsyncLockedDictionary<string, int>();
        (await dict.TryAddAsync("a", 1)).Should().BeTrue();
        (await dict.TryAddAsync("a", 2)).Should().BeFalse();
    }

    [Fact(Timeout = 10000)]
    public async Task RemoveAsync_ExistingKey_ReturnsValue_Removes()
    {
        var dict = new AsyncLockedDictionary<string, int>();
        await dict.TryAddAsync("a", 42);

        var removed = await dict.RemoveAsync("a");
        removed.Should().Be(42);

        (await dict.RemoveAsync("a")).Should().Be(0, "已删除返回 default(int)");
    }

    [Fact(Timeout = 10000)]
    public async Task SnapshotAsync_ReturnsIndependentCopy()
    {
        var dict = new AsyncLockedDictionary<string, int>();
        await dict.TryAddAsync("a", 1);
        await dict.TryAddAsync("b", 2);

        var snap = await dict.SnapshotAsync();
        snap["a"].Should().Be(1);
        snap["b"].Should().Be(2);

        await dict.TryAddAsync("c", 3);
        snap.ContainsKey("c").Should().BeFalse("快照应独立于后续修改");
    }

    [Fact(Timeout = 10000)]
    public async Task AddOrUpdateAsync_NewKey_UsesDefaultExisting()
    {
        var dict = new AsyncLockedDictionary<string, int>();
        var v = await dict.AddOrUpdateAsync("a", (_, existing) => existing + 1);
        v.Should().Be(1, "新 key 时 existing=default(int)=0, 返回 0+1=1");

        var v2 = await dict.AddOrUpdateAsync("a", (_, existing) => existing + 1);
        v2.Should().Be(2);
    }

    [Fact(Timeout = 10000)]
    public async Task AddOrUpdateAsync_RespectsComparer()
    {
        var dict = new AsyncLockedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await dict.TryAddAsync("ABC", 1);
        var v = await dict.AddOrUpdateAsync("abc", (_, existing) => existing + 10);
        v.Should().Be(11, "OrdinalIgnoreCase 下 abc 与 ABC 同 key");
    }

    // ===== 2. 并发正确性 (Concurrent Correctness) =====

    [Fact(Timeout = 10000)]
    public async Task AddOrUpdateAsync_ConcurrentSameKey_NoLostUpdates()
    {
        var dict = new AsyncLockedDictionary<string, int>();
        const int N = 50;
        const int M = 20;

        var startGate = new TaskCompletionSource<bool>();
        var tasks = Enumerable.Range(0, N).Select(async _ =>
        {
            await startGate.Task;
            for (var i = 0; i < M; i++)
            {
                await dict.AddOrUpdateAsync("counter", (_, existing) => existing + 1);
            }
        }).ToArray();

        startGate.SetResult(true);
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        var snap = await dict.SnapshotAsync();
        snap["counter"].Should().Be(N * M, "N 个线程各 M 次 +1, 无丢失更新");
    }

    [Fact(Timeout = 10000)]
    public async Task TryAddAsync_ConcurrentDistinctKeys_AllPresent()
    {
        var dict = new AsyncLockedDictionary<int, int>();
        const int N = 100;

        var startGate = new TaskCompletionSource<bool>();
        var tasks = Enumerable.Range(0, N).Select(async i =>
        {
            await startGate.Task;
            await dict.TryAddAsync(i, i * 10);
        }).ToArray();

        startGate.SetResult(true);
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        var snap = await dict.SnapshotAsync();
        snap.Count.Should().Be(N);
        for (var i = 0; i < N; i++)
            snap[i].Should().Be(i * 10);
    }

    // ===== 3. 分片锁语义 (Striped Lock - 不同 key 并行) =====

    /// <summary>
    /// 红测试:不同 key 的异步 factory 必须能并行执行。
    /// 单全局锁下 factory1 持锁 await tcs1, factory2 无法获取锁进入 factory,
    /// 而 tcs1 只在 factory2 内被 SetResult → 互等死锁 → 超时红。
    /// ConcurrentDictionary 分片锁下两个 factory 并行, 互相 SetResult 后都完成 → 绿。
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task GetOrAddAsync_DifferentKeys_AsyncFactoriesExecuteInParallel()
    {
        var dict = new AsyncLockedDictionary<string, int>();
        var tcs1 = new TaskCompletionSource<int>();
        var tcs2 = new TaskCompletionSource<int>();

        var task1 = dict.GetOrAddAsync("a", async _ =>
        {
            tcs2.TrySetResult(2);
            return await tcs1.Task;
        });
        var task2 = dict.GetOrAddAsync("b", async _ =>
        {
            tcs1.TrySetResult(1);
            return await tcs2.Task;
        });

        var results = await Task.WhenAll(task1.AsTask(), task2.AsTask())
            .WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal(1, 2);
    }

    /// <summary>
    /// 不同 key 的同步 AddOrUpdate 在高并发下应全部成功且值正确(分片锁不改变正确性, 只提升并行度)。
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task AddOrUpdateAsync_ConcurrentDistinctKeys_AllCorrect()
    {
        var dict = new AsyncLockedDictionary<int, int>();
        const int N = 100;
        const int M = 10;

        var startGate = new TaskCompletionSource<bool>();
        var tasks = Enumerable.Range(0, N).Select(async i =>
        {
            await startGate.Task;
            for (var j = 0; j < M; j++)
            {
                await dict.AddOrUpdateAsync(i, (_, existing) => existing + 1);
            }
        }).ToArray();

        startGate.SetResult(true);
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        var snap = await dict.SnapshotAsync();
        snap.Count.Should().Be(N);
        for (var i = 0; i < N; i++)
            snap[i].Should().Be(M);
    }
}

#pragma warning restore VSTHRD003
