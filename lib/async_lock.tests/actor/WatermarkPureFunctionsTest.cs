namespace Core.Utils;

/// <summary>
/// 水位线判断纯函数确定性单元测试 — 验证 CheckHighWatermark/CheckCriticalWatermark,不依赖时序/不启动 Actor。
/// <para>覆盖:无背压(null)、未达阈值、等于阈值(边界)、超过阈值、零计数、默认水位线比例。</para>
/// <para>static 方法通过 PureFuncActor(AskRetryPureFunctionsTest 中定义)调用,不实例化 Actor,无时序依赖。</para>
/// </summary>
public class WatermarkPureFunctionsTest {
    private static ActorBackpressure Bp(int cap, int high, int crit)
        => new(Capacity: cap, HighWatermark: high, CriticalWatermark: crit);

    #region CheckHighWatermark — 高水位线判断纯函数

    /// <summary>无背压(null) → false(总返回 false,无水位线概念)</summary>
    [Fact]
    public void HighWatermark_NullBp_ReturnsFalse() {
        PureFuncActor.CheckHighWatermark(null, 100).Should().BeFalse("无背压配置,不存在水位线");
    }

    /// <summary>inputCount < high → false(未达高水位线)</summary>
    [Fact]
    public void HighWatermark_BelowThreshold_ReturnsFalse() {
        var bp = Bp(10, 5, 8);
        PureFuncActor.CheckHighWatermark(bp, 4).Should().BeFalse("4 < 5,未达高水位线");
    }

    /// <summary>inputCount == high → true(边界,等于阈值即触发)</summary>
    [Fact]
    public void HighWatermark_AtThreshold_ReturnsTrue() {
        var bp = Bp(10, 5, 8);
        PureFuncActor.CheckHighWatermark(bp, 5).Should().BeTrue("5 == 5,达到高水位线(边界)");
    }

    /// <summary>inputCount > high → true(超过阈值)</summary>
    [Fact]
    public void HighWatermark_AboveThreshold_ReturnsTrue() {
        var bp = Bp(10, 5, 8);
        PureFuncActor.CheckHighWatermark(bp, 7).Should().BeTrue("7 > 5,超过高水位线");
    }

    /// <summary>inputCount=0 → false(无输入,未达阈值)</summary>
    [Fact]
    public void HighWatermark_ZeroCount_ReturnsFalse() {
        var bp = Bp(10, 5, 8);
        PureFuncActor.CheckHighWatermark(bp, 0).Should().BeFalse("0 < 5,未达高水位线");
    }

    /// <summary>inputCount=0 且 high=0 → true(边界,阈值为0时0>=0)</summary>
    [Fact]
    public void HighWatermark_ZeroCountZeroThreshold_ReturnsTrue() {
        var bp = Bp(10, 0, 5);
        PureFuncActor.CheckHighWatermark(bp, 0).Should().BeTrue("0 >= 0,阈值0时零计数触发");
    }

    /// <summary>默认水位线比例 — Capacity=100,默认High=80</summary>
    [Fact]
    public void HighWatermark_DefaultRatio_Capacity100_High80() {
        var bp = new ActorBackpressure(Capacity: 100);
        PureFuncActor.CheckHighWatermark(bp, 79).Should().BeFalse("79 < 80(默认80%)");
        PureFuncActor.CheckHighWatermark(bp, 80).Should().BeTrue("80 >= 80(默认80%)");
    }

    #endregion

    #region CheckCriticalWatermark — 危险水位线判断纯函数

    /// <summary>无背压(null) → false</summary>
    [Fact]
    public void CriticalWatermark_NullBp_ReturnsFalse() {
        PureFuncActor.CheckCriticalWatermark(null, 100).Should().BeFalse("无背压配置,不存在水位线");
    }

    /// <summary>inputCount < critical → false</summary>
    [Fact]
    public void CriticalWatermark_BelowThreshold_ReturnsFalse() {
        var bp = Bp(10, 5, 8);
        PureFuncActor.CheckCriticalWatermark(bp, 7).Should().BeFalse("7 < 8,未达危险水位线");
    }

    /// <summary>inputCount == critical → true(边界)</summary>
    [Fact]
    public void CriticalWatermark_AtThreshold_ReturnsTrue() {
        var bp = Bp(10, 5, 8);
        PureFuncActor.CheckCriticalWatermark(bp, 8).Should().BeTrue("8 == 8,达到危险水位线(边界)");
    }

    /// <summary>inputCount > critical → true</summary>
    [Fact]
    public void CriticalWatermark_AboveThreshold_ReturnsTrue() {
        var bp = Bp(10, 5, 8);
        PureFuncActor.CheckCriticalWatermark(bp, 9).Should().BeTrue("9 > 8,超过危险水位线");
    }

    /// <summary>inputCount=0 → false</summary>
    [Fact]
    public void CriticalWatermark_ZeroCount_ReturnsFalse() {
        var bp = Bp(10, 5, 8);
        PureFuncActor.CheckCriticalWatermark(bp, 0).Should().BeFalse("0 < 8,未达危险水位线");
    }

    /// <summary>默认水位线比例 — Capacity=100,默认Critical=95</summary>
    [Fact]
    public void CriticalWatermark_DefaultRatio_Capacity100_Critical95() {
        var bp = new ActorBackpressure(Capacity: 100);
        PureFuncActor.CheckCriticalWatermark(bp, 94).Should().BeFalse("94 < 95(默认95%)");
        PureFuncActor.CheckCriticalWatermark(bp, 95).Should().BeTrue("95 >= 95(默认95%)");
    }

    #endregion

    #region 高水位线 vs 危险水位线 关系

    /// <summary>达到危险水位线时必然达到高水位线(critical >= high)</summary>
    [Fact]
    public void CriticalImpliesHigh_WhenCriticalAboveHigh() {
        var bp = Bp(20, 5, 10);
        for (var count = 0; count <= 20; count++) {
            var critical = PureFuncActor.CheckCriticalWatermark(bp, count);
            var high = PureFuncActor.CheckHighWatermark(bp, count);
            if (critical) {
                high.Should().BeTrue($"count={count}: 达到危险水位线必然达到高水位线");
            }
        }
    }

    #endregion
}
