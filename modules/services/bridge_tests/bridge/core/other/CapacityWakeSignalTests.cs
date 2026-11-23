namespace Bridge.Tests;

/// <summary>
/// CapacityWakeSignal 单元测试
/// 测试容量唤醒信号的正常唤醒、超时返回 false、取消返回 false
/// </summary>
public sealed class CapacityWakeSignalTests {
    /// <summary>TASK071 #10 语义变更锁（对齐 TS controller 轮换）— wake 只影响在途等待者：
    /// 先 wake 后 sleep 拿到的是换新信号代，应睡满超时返回 false（旧 SemaphoreSlim 残留令牌会立即 true）。</summary>
    [Fact]
    public async Task SleepUntilCapacityWakesAsync_AfterWakeUp_NewGenerationSleepsFull_ReturnsFalse() {
        using var signal = new CapacityWakeSignal();
        signal.WakeUp();

        var result = await signal.SleepUntilCapacityWakesAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(true);

        result.Should().BeFalse("wake 后换新信号代 — 无在途等待者时信号丢弃，不应陈旧唤醒");
    }

    [Fact]
    public async Task SleepUntilCapacityWakesAsync_WakeUpWhileSleeping_ReturnsTrue() {
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
    public async Task SleepUntilCapacityWakesAsync_Timeout_ReturnsFalse() {
        // Arrange
        using var signal = new CapacityWakeSignal();

        // Act — very short timeout, no wake-up
        var result = await signal.SleepUntilCapacityWakesAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(true);

        // Assert
        result.Should().BeFalse("超时未唤醒应返回 false");
    }

    [Fact]
    public async Task SleepUntilCapacityWakesAsync_Cancelled_ReturnsFalse() {
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
    public async Task SleepUntilCapacityWakesAsync_CancelledDuringWait_ReturnsFalse() {
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

    /// <summary>连续 wake 等价一次（TS 轮换语义 — 只换新代，无令牌累积）。</summary>
    [Fact]
    public async Task WakeUp_MultipleTimes_EquivalentToSingleWake() {
        using var signal = new CapacityWakeSignal();
        for (var i = 0; i < 3; i++) {
            signal.WakeUp();
        }

        // 无在途等待者 — 三次 wake 的信号全部随代丢弃
        var result = await signal.SleepUntilCapacityWakesAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(true);
        result.Should().BeFalse("连续 wake 无等待者时不应陈旧唤醒");
    }

    /// <summary>多个并发等待者 — 一次 wake 全部唤醒（TS 每 sleep 私有 merged signal，abort 当前代全醒）。</summary>
    [Fact]
    public async Task WakeUp_MultipleWaiters_AllWoken() {
        using var signal = new CapacityWakeSignal();
        using var cts = new CancellationTokenSource();
        var sleep1 = signal.SleepUntilCapacityWakesAsync(TimeSpan.FromSeconds(10), cts.Token);
        var sleep2 = signal.SleepUntilCapacityWakesAsync(TimeSpan.FromSeconds(10), cts.Token);
        await Task.Delay(50).ConfigureAwait(true);

        signal.WakeUp();

        (await sleep1.ConfigureAwait(true)).Should().BeTrue("等待者 1 应被唤醒");
        (await sleep2.ConfigureAwait(true)).Should().BeTrue("等待者 2 应被唤醒（旧单放行语义只醒一个）");
    }

    [Fact]
    public void Dispose_DoesNotThrow() {
        // Arrange
        var signal = new CapacityWakeSignal();

        // Act
        var ex = Record.Exception(() => signal.Dispose());

        // Assert
        ex.Should().BeNull();
    }
}