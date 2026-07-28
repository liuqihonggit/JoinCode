
#pragma warning disable JCC3010, JCC3011, JCC3012

namespace Core.Tests.State;

/// <summary>
/// Store&lt;TState&gt; 即发即弃持久化测试
/// 验证 SetState/SetStateAsync 触发持久化、Dispose 取消持久化、异常不崩溃
/// </summary>
public sealed class StorePersistenceTests
{
    /// <summary>
    /// 模拟持久化实现 — 记录调用次数和最后一次 CancellationToken
    /// </summary>
    private sealed class MockPersistence : IStorePersistence<string>
    {
        private readonly SemaphoreSlim _saveSignal = new(0);
        public int SaveCallCount;
        public CancellationToken LastCancellationToken;

        public Task SaveAsync(string state, CancellationToken ct)
        {
            Interlocked.Increment(ref SaveCallCount);
            LastCancellationToken = ct;
            _saveSignal.Release();
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(CancellationToken ct) => Task.FromResult<string?>(null);

        public Task WaitSaveAsync(TimeSpan timeout) => _saveSignal.WaitAsync(timeout);
    }

    /// <summary>
    /// 慢速持久化实现 — SaveAsync 永远不主动完成，仅靠 CancellationToken 取消
    /// </summary>
    private sealed class SlowPersistence : IStorePersistence<string>
    {
        private readonly SemaphoreSlim _enteredSignal = new(0);

        public async Task SaveAsync(string state, CancellationToken ct)
        {
            _enteredSignal.Release();
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(true);
        }

        public Task<string?> LoadAsync(CancellationToken ct) => Task.FromResult<string?>(null);

        public Task WaitEnteredAsync(TimeSpan timeout) => _enteredSignal.WaitAsync(timeout);
    }

    /// <summary>
    /// 抛异常持久化实现 — SaveAsync 总是抛出异常
    /// </summary>
    private sealed class ThrowingPersistence : IStorePersistence<string>
    {
        private readonly SemaphoreSlim _enteredSignal = new(0);

        public Task SaveAsync(string state, CancellationToken ct)
        {
            _enteredSignal.Release();
            throw new InvalidOperationException("模拟持久化失败");
        }

        public Task<string?> LoadAsync(CancellationToken ct) => Task.FromResult<string?>(null);

        public Task WaitEnteredAsync(TimeSpan timeout) => _enteredSignal.WaitAsync(timeout);
    }

    [Fact(Timeout = 5000)]
    public async Task SetState_WithPersistence_CallsSaveAsync()
    {
        // Arrange
        var persistence = new MockPersistence();
        var store = new Store<string>("initial", persistence);

        // Act
        store.SetState(s => s + "-updated");
        await persistence.WaitSaveAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

        // Assert
        Assert.Equal(1, persistence.SaveCallCount);
        Assert.Equal("initial-updated", store.GetState());

        store.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task SetStateAsync_WithPersistence_CallsSaveAsync()
    {
        // Arrange
        var persistence = new MockPersistence();
        var store = new Store<string>("initial", persistence);

        // Act
        await store.SetStateAsync(s => Task.FromResult(s + "-async"), CancellationToken.None).ConfigureAwait(true);
        await persistence.WaitSaveAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

        // Assert
        Assert.Equal(1, persistence.SaveCallCount);
        Assert.Equal("initial-async", store.GetState());

        store.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Dispose_CancelsPendingPersistence()
    {
        // Arrange
        var persistence = new MockPersistence();
        var store = new Store<string>("initial", persistence);

        // Act — SetState 触发即发即弃持久化
        store.SetState(s => s + "-updated");
        await persistence.WaitSaveAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

        // 捕获最后一次 SaveAsync 收到的 CancellationToken
        var ct = persistence.LastCancellationToken;

        // Dispose 应取消 _disposeCts
        store.Dispose();

        // Assert — Dispose 后 CancellationToken 应被取消
        Assert.True(ct.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public async Task SetState_PersistenceThrows_DoesNotCrash()
    {
        // Arrange
        var persistence = new ThrowingPersistence();
        var store = new Store<string>("initial", persistence);

        // Act — SaveAsync 抛异常，但 Store 不应崩溃
        store.SetState(s => s + "-updated");

        // 等待即发即弃任务进入 SaveAsync
        await persistence.WaitEnteredAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

        // Assert — Store 仍然可用，状态已更新
        Assert.Equal("initial-updated", store.GetState());

        // 再次 SetState 也不应崩溃
        store.SetState(s => s + "-again");
        await persistence.WaitEnteredAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);
        Assert.Equal("initial-updated-again", store.GetState());

        store.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task SetState_PersistenceTimesOut_DoesNotCrash()
    {
        // Arrange — SlowPersistence 的 SaveAsync 永远不主动完成
        var persistence = new SlowPersistence();
        var store = new Store<string>("initial", persistence);

        // Act — SetState 触发即发即弃持久化，SaveAsync 会卡住
        store.SetState(s => s + "-updated");

        // 等待即发即弃任务已启动并进入 SaveAsync
        await persistence.WaitEnteredAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

        // Assert — Store 仍然可用，状态已更新（持久化在后台卡住不影响主流程）
        Assert.Equal("initial-updated", store.GetState());

        // Dispose 取消卡住的持久化任务
        store.Dispose();

        // Dispose 后再验证状态不变
        Assert.Equal("initial-updated", store.GetState());
    }
}
