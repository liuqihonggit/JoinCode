namespace JoinCode.CodeIndex.Tests;

public sealed class TimeoutLockTests : IDisposable
{
    private readonly TimeoutLock _lock;

    public TimeoutLockTests()
    {
        _lock = new TimeoutLock("TestLock", TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_ReleasedByDisposal_ReleasesLock()
    {
        var releaser = await _lock.AcquireAsync(CancellationToken.None).ConfigureAwait(true);
        releaser.Dispose();

        var second = await _lock.AcquireAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(second);
        second.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_Timeout_ThrowsTimeoutException()
    {
        using var releaser = await _lock.AcquireAsync(CancellationToken.None).ConfigureAwait(true);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            using var _ = await _lock.AcquireAsync(CancellationToken.None, TimeSpan.FromMilliseconds(10)).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Fact]
    public void AcquireSync_ReleasedByDisposal_ReleasesLock()
    {
        var releaser = _lock.Acquire();
        releaser.Dispose();

        var second = _lock.Acquire();
        Assert.NotNull(second);
        second.Dispose();
    }

    [Fact]
    public void AcquireSync_Timeout_ThrowsTimeoutException()
    {
        using var releaser = _lock.Acquire();

        Assert.Throws<TimeoutException>(() => _lock.Acquire(TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void Constructor_NullLockName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TimeoutLock(null!));
    }

    [Fact]
    public void Acquire_AfterDispose_ThrowsObjectDisposedException()
    {
        var l = new TimeoutLock("DisposedLock", TimeSpan.FromSeconds(1));
        l.Dispose();

        Assert.Throws<ObjectDisposedException>(() => l.Acquire());
    }

    [Fact]
    public async Task AcquireAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var l = new TimeoutLock("DisposedLockAsync", TimeSpan.FromSeconds(1));
        l.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            using var _ = await l.AcquireAsync(CancellationToken.None).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task AcquireAsync_LogsMessages_WhenLoggerProvided()
    {
        var messages = new List<string>();
        var l = new TimeoutLock("LoggedLock", TimeSpan.FromSeconds(5), messages.Add);

        using (await l.AcquireAsync(CancellationToken.None).ConfigureAwait(true))
        {
        }

        Assert.Contains(messages, m => m.Contains("Acquiring"));
        Assert.Contains(messages, m => m.Contains("Acquired"));
        Assert.Contains(messages, m => m.Contains("Released"));
        l.Dispose();
    }
}
