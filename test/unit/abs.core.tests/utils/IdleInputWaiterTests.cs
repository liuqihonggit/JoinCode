namespace Abs.Tests.Utils;

/// <summary>
/// IdleInputWaiter 单元测试 — 验证输入就绪、轮询后就绪、空闲超时、取消传播。
/// </summary>
public sealed class IdleInputWaiterTests {
    [Fact]
    public async Task WaitAsync_InputAvailableImmediately_ReturnsTrue() {
        var waiter = new IdleInputWaiter(() => true, _ => Task.CompletedTask);

        var ready = await waiter.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(ready);
    }

    [Fact]
    public async Task WaitAsync_InputAppearsAfterPolls_ReturnsTrue() {
        var remainingPolls = 3;
        var waiter = new IdleInputWaiter(
            () => remainingPolls-- <= 0,
            _ => Task.Delay(10));

        var ready = await waiter.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(ready);
    }

    [Fact]
    public async Task WaitAsync_NoInputWithinIdleTimeout_ReturnsFalse() {
        var start = Environment.TickCount64;
        var waiter = new IdleInputWaiter(() => false, _ => Task.Delay(20));

        var ready = await waiter.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        var elapsed = Environment.TickCount64 - start;

        Assert.False(ready);
        Assert.True(elapsed >= 100, $"空闲等待应至少持续 100ms，实际 {elapsed}ms");
    }

    [Fact]
    public async Task WaitAsync_Cancelled_Throws() {
        using var cts = new CancellationTokenSource();
        var waiter = new IdleInputWaiter(() => false, _ => Task.Delay(20));

        var waiting = waiter.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task WaitAsync_ZeroIdleTimeout_ChecksInputOnce() {
        var checks = 0;
        var waiter = new IdleInputWaiter(() => { checks++; return true; }, _ => Task.CompletedTask);

        var ready = await waiter.WaitAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.True(ready);
        Assert.Equal(1, checks);
    }
}
