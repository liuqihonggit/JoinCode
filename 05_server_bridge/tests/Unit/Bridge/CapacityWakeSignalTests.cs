namespace Bridge.Tests;

/// <summary>
/// CapacityWakeSignal 单元测试
/// 测试容量唤醒信号的正常唤醒、超时返回 false、取消返回 false
/// </summary>
public sealed class CapacityWakeSignalTests
{
    [Fact]
    public async Task SleepUntilCapacityWakesAsync_AfterWakeUp_ReturnsTrue()
    {
        // Arrange
        using var signal = new CapacityWakeSignal();
        signal.WakeUp();

        // Act
        var result = await signal.SleepUntilCapacityWakesAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        // Assert
        result.Should().BeTrue("唤醒后等待应返回 true");
    }

    [Fact]
    public async Task SleepUntilCapacityWakesAsync_WakeUpWhileSleeping_ReturnsTrue()
    {
        // Arrange
        using var signal = new CapacityWakeSignal();
        using var cts = new CancellationTokenSource();

        var sleepTask = signal.SleepUntilCapacityWakesAsync(TimeSpan.FromSeconds(5), cts.Token);

        // Act — wake up after a brief delay
        await Task.Delay(50).ConfigureAwait(true);
        signal.WakeUp();

        var result = await sleepTask.ConfigureAwait(true);

        // Assert
        result.Should().BeTrue("等待期间唤醒应返回 true");
    }

    [Fact]
    public async Task SleepUntilCapacityWakesAsync_Timeout_ReturnsFalse()
    {
        // Arrange
        using var signal = new CapacityWakeSignal();

        // Act — very short timeout, no wake-up
        var result = await signal.SleepUntilCapacityWakesAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(true);

        // Assert
        result.Should().BeFalse("超时未唤醒应返回 false");
    }

    [Fact]
    public async Task SleepUntilCapacityWakesAsync_Cancelled_ReturnsFalse()
    {
        // Arrange
        using var signal = new CapacityWakeSignal();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await signal.SleepUntilCapacityWakesAsync(TimeSpan.FromSeconds(5), cts.Token).ConfigureAwait(true);

        // Assert
        result.Should().BeFalse("取消应返回 false");
    }

    [Fact]
    public async Task SleepUntilCapacityWakesAsync_CancelledDuringWait_ReturnsFalse()
    {
        // Arrange
        using var signal = new CapacityWakeSignal();
        using var cts = new CancellationTokenSource();

        var sleepTask = signal.SleepUntilCapacityWakesAsync(TimeSpan.FromSeconds(10), cts.Token);

        // Act — cancel after a brief delay
        await Task.Delay(50).ConfigureAwait(true);
        await cts.CancelAsync().ConfigureAwait(true);

        var result = await sleepTask.ConfigureAwait(true);

        // Assert
        result.Should().BeFalse("等待期间取消应返回 false");
    }

    [Fact]
    public async Task WakeUp_MultipleTimes_AllWakeupsConsumed()
    {
        // Arrange
        using var signal = new CapacityWakeSignal();
        const int wakeCount = 3;

        for (var i = 0; i < wakeCount; i++)
        {
            signal.WakeUp();
        }

        // Act & Assert — each sleep should succeed
        for (var i = 0; i < wakeCount; i++)
        {
            var result = await signal.SleepUntilCapacityWakesAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
            result.Should().BeTrue($"第 {i + 1} 次唤醒应返回 true");
        }
    }

    [Fact]
    public async Task SleepUntilCapacityWakesAsync_AfterAllWakeupsConsumed_TimeoutReturnsFalse()
    {
        // Arrange
        using var signal = new CapacityWakeSignal();
        signal.WakeUp();

        // Act — consume the single wakeup
        var first = await signal.SleepUntilCapacityWakesAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
        first.Should().BeTrue();

        // No more wakeups — should timeout
        var second = await signal.SleepUntilCapacityWakesAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(true);

        // Assert
        second.Should().BeFalse("唤醒已消费完，再次等待应超时返回 false");
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var signal = new CapacityWakeSignal();

        // Act
        var ex = Record.Exception(() => signal.Dispose());

        // Assert
        ex.Should().BeNull();
    }
}
