namespace JoinCode.CodeIndex.Tests;

public sealed class TimeoutLockTests : IDisposable {
    private readonly TimeoutLock _lock;

    public TimeoutLockTests() {
        _lock = new TimeoutLock("TestLock", TimeSpan.FromSeconds(1));
    }

    public void Dispose() => _lock.DisposeSafe();

    [Fact]
    public async Task AcquireAsync_ReleasedByDisposal_ReleasesLock() {
        var releaser = await _lock.AcquireAsync(CancellationToken.None).ConfigureAwait(true);
        releaser.Dispose();

        using var second = await _lock.AcquireAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task AcquireAsync_Timeout_ThrowsTimeoutException() {
        using var releaser = await _lock.AcquireAsync(CancellationToken.None).ConfigureAwait(true);

        await Assert.ThrowsAsync<TimeoutException>(async () => {
            using var _ = await _lock.AcquireAsync(CancellationToken.None, TimeSpan.FromMilliseconds(10)).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Fact]
    public void AcquireSync_ReleasedByDisposal_ReleasesLock() {
        var releaser = _lock.Acquire();
        releaser.Dispose();

        using var second = _lock.Acquire();
        Assert.NotNull(second);
    }

    [Fact]
    public void AcquireSync_Timeout_ThrowsTimeoutException() {
        using var releaser = _lock.Acquire();

        Assert.Throws<TimeoutException>(() => _lock.Acquire(TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void Constructor_NullLockName_Throws() {
        Assert.Throws<ArgumentNullException>(() => new TimeoutLock(null!));
    }

    [Fact]
    public void Acquire_AfterDispose_ThrowsObjectDisposedException() {
        var l = new TimeoutLock("DisposedLock", TimeSpan.FromSeconds(1));
        l.Dispose();

        Assert.Throws<ObjectDisposedException>(() => l.Acquire());
    }

    [Fact]
    public async Task AcquireAsync_AfterDispose_ThrowsObjectDisposedException() {
        var l = new TimeoutLock("DisposedLockAsync", TimeSpan.FromSeconds(1));
        l.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => {
            using var _ = await l.AcquireAsync(CancellationToken.None).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task AcquireAsync_LogsMessages_WhenLoggerProvided() {
        var messages = new List<string>();
        using var l = new TimeoutLock("LoggedLock", TimeSpan.FromSeconds(5), messages.Add);

        using (await l.AcquireAsync(CancellationToken.None).ConfigureAwait(true)) {
        }

        Assert.Contains(messages, m => m.Contains("Acquiring"));
        Assert.Contains(messages, m => m.Contains("Acquired"));
        Assert.Contains(messages, m => m.Contains("Released"));
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void Constructor_NegativeDefaultTimeout_ThrowsArgumentOutOfRangeException() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeoutLock("NegDefault", TimeSpan.FromSeconds(-1)));
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public async Task AcquireAsync_NegativeTimeout_ThrowsArgumentOutOfRangeException() {
        using var l = new TimeoutLock("NegAsync");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => {
            using var _ = await l.AcquireAsync(CancellationToken.None, TimeSpan.FromSeconds(-1)).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void Acquire_NegativeTimeout_ThrowsArgumentOutOfRangeException() {
        using var l = new TimeoutLock("NegSync");
        Assert.Throws<ArgumentOutOfRangeException>(() => l.Acquire(TimeSpan.FromSeconds(-1)));
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void Constructor_ZeroDefaultTimeout_IsAllowed() {
        // TimeSpan.Zero 合法(立即超时策略),不应抛异常
        using var l = new TimeoutLock("ZeroDefault", TimeSpan.Zero);
        Assert.NotNull(l);
    }
}