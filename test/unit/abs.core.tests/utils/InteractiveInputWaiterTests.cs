namespace Abs.Tests.Utils;

/// <summary>
/// InteractiveInputWaiter 单元测试 — 验证输入就绪、后台唤醒、优先级、轮询等待、取消传播。
/// </summary>
public sealed class InteractiveInputWaiterTests {
    [Fact]
    public async Task WaitAsync_InputAvailableImmediately_ReturnsInputReady() {
        var waiter = new InteractiveInputWaiter(() => true, () => false, _ => Task.CompletedTask);

        var outcome = await waiter.WaitAsync(CancellationToken.None);

        Assert.Equal(InputWaitOutcome.InputReady, outcome);
    }

    [Fact]
    public async Task WaitAsync_BackgroundSignal_ReturnsInterrupted() {
        var waiter = new InteractiveInputWaiter(() => false, () => true, _ => Task.CompletedTask);

        var outcome = await waiter.WaitAsync(CancellationToken.None);

        Assert.Equal(InputWaitOutcome.Interrupted, outcome);
    }

    [Fact]
    public async Task WaitAsync_InputAndSignalBothReady_PrefersInput() {
        var waiter = new InteractiveInputWaiter(() => true, () => true, _ => Task.CompletedTask);

        var outcome = await waiter.WaitAsync(CancellationToken.None);

        Assert.Equal(InputWaitOutcome.InputReady, outcome);
    }

    [Fact]
    public async Task WaitAsync_InputAppearsAfterPolls_ReturnsInputReady() {
        var remainingPolls = 3;
        var waiter = new InteractiveInputWaiter(
            () => remainingPolls-- <= 0,
            () => false,
            _ => Task.Delay(10));

        var outcome = await waiter.WaitAsync(CancellationToken.None);

        Assert.Equal(InputWaitOutcome.InputReady, outcome);
    }

    [Fact]
    public async Task WaitAsync_SignalAppearsAfterPolls_ReturnsInterrupted() {
        var remainingPolls = 2;
        var waiter = new InteractiveInputWaiter(
            () => false,
            () => remainingPolls-- <= 0,
            _ => Task.Delay(10));

        var outcome = await waiter.WaitAsync(CancellationToken.None);

        Assert.Equal(InputWaitOutcome.Interrupted, outcome);
    }

    [Fact]
    public async Task WaitAsync_Cancelled_Throws() {
        using var cts = new CancellationTokenSource();
        var waiter = new InteractiveInputWaiter(() => false, () => false, _ => Task.Delay(20));

        var waiting = waiter.WaitAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
