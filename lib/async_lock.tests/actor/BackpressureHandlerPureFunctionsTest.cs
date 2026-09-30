namespace Core.Utils;

/// <summary>
/// CreateBackpressureHandler 纯函数确定性单元测试 — 验证延迟判断和同步路径行为,不依赖时序。
/// <para>覆盖:ShouldDelayRetry 边界(零/正/负/最大值)、零延迟同步调用 resend、resend 异常吞掉。</para>
/// <para>static 方法通过 PureFuncActor(AskRetryPureFunctionsTest 中定义)调用,不实例化 Actor,无时序依赖。</para>
/// </summary>
[Trait("Category", "Deterministic")]
public class BackpressureHandlerPureFunctionsTest {
    private static BackpressureSignal Signal(TimeSpan delay)
        => new(0, "src", "tgt", WatermarkLevel.High, delay, 0);

    #region ShouldDelayRetry — 延迟判断纯函数

    /// <summary>零延迟 → false(立即重试,不走 Task.Delay)</summary>
    [Fact]
    public void ShouldDelay_ZeroDelay_ReturnsFalse() {
        PureFuncActor.ShouldDelayRetry(TimeSpan.Zero).Should().BeFalse("零延迟=立即重试");
    }

    /// <summary>正延迟 → true(需要延迟后重试)</summary>
    [Fact]
    public void ShouldDelay_PositiveDelay_ReturnsTrue() {
        PureFuncActor.ShouldDelayRetry(TimeSpan.FromMilliseconds(100)).Should().BeTrue("正延迟=延迟重试");
    }

    /// <summary>负延迟 → false(视为立即重试)</summary>
    [Fact]
    public void ShouldDelay_NegativeDelay_ReturnsFalse() {
        PureFuncActor.ShouldDelayRetry(TimeSpan.FromMilliseconds(-1)).Should().BeFalse("负延迟=立即重试");
    }

    /// <summary>最大值 → true(极端大延迟仍需延迟)</summary>
    [Fact]
    public void ShouldDelay_MaxValue_ReturnsTrue() {
        PureFuncActor.ShouldDelayRetry(TimeSpan.MaxValue).Should().BeTrue("MaxValue=延迟重试");
    }

    /// <summary>1 tick → true(最小正延迟仍需延迟)</summary>
    [Fact]
    public void ShouldDelay_OneTick_ReturnsTrue() {
        PureFuncActor.ShouldDelayRetry(TimeSpan.FromTicks(1)).Should().BeTrue("1 tick=延迟重试");
    }

    #endregion

    #region CreateBackpressureHandler 同步路径 — 零延迟确定性测试

    /// <summary>零延迟 → 同步路径立即调用 resend(不启动 Task.Run,确定性)</summary>
    [Fact]
    public void Handler_ZeroDelay_CallsResendImmediately() {
        var called = false;
        var handler = PureFuncActor.CreateBackpressureHandler(() => called = true);
        handler(Signal(TimeSpan.Zero));
        called.Should().BeTrue("零延迟走同步路径,立即调用 resend");
    }

    /// <summary>零延迟 + resend 抛异常 → 异常吞掉不传播(确定性)</summary>
    [Fact]
    public void Handler_ZeroDelay_ResendThrows_ExceptionSwallowed() {
        var handler = PureFuncActor.CreateBackpressureHandler(() => throw new InvalidOperationException("test"));
        var act = () => handler(Signal(TimeSpan.Zero));
        act.Should().NotThrow("resend 异常应被吞掉,不传播给调用方");
    }

    /// <summary>零延迟 + resend 调用多次 → 每次都同步执行(确定性)</summary>
    [Fact]
    public void Handler_ZeroDelay_MultipleCalls_EachExecutes() {
        var callCount = 0;
        var handler = PureFuncActor.CreateBackpressureHandler(() => Interlocked.Increment(ref callCount));
        for (var i = 0; i < 5; i++) {
            handler(Signal(TimeSpan.Zero));
        }
        callCount.Should().Be(5, "5 次零延迟信号各同步调用一次 resend");
    }

    #endregion
}
