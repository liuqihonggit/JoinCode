namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// V1ReplBridgeTransport.IsStreamEvent 确定性测试 — 字符串包含判定，纯函数无时序依赖。
/// </summary>
public class V1ReplBridgeTransportTest {
    /// <summary>type 为 stream_event 时返回 true。</summary>
    [Fact]
    public void IsStreamEvent_TypeIsStreamEvent_ReturnsTrue() {
        V1ReplBridgeTransport.IsStreamEvent("{\"type\":\"stream_event\",\"data\":\"x\"}")
            .Should().BeTrue();
    }

    /// <summary>type 为其他值时返回 false。</summary>
    [Fact]
    public void IsStreamEvent_TypeIsOther_ReturnsFalse() {
        V1ReplBridgeTransport.IsStreamEvent("{\"type\":\"other\"}")
            .Should().BeFalse();
    }

    /// <summary>缺少 type 字段时返回 false（即使含 stream_event 字符串）。</summary>
    [Fact]
    public void IsStreamEvent_NoTypeField_ReturnsFalse() {
        V1ReplBridgeTransport.IsStreamEvent("{\"event\":\"stream_event\"}")
            .Should().BeFalse();
    }

    /// <summary>缺少 stream_event 字符串时返回 false。</summary>
    [Fact]
    public void IsStreamEvent_NoStreamEventToken_ReturnsFalse() {
        V1ReplBridgeTransport.IsStreamEvent("{\"type\":\"ping\"}")
            .Should().BeFalse();
    }

    /// <summary>空字符串返回 false。</summary>
    [Fact]
    public void IsStreamEvent_EmptyString_ReturnsFalse() {
        V1ReplBridgeTransport.IsStreamEvent("")
            .Should().BeFalse();
    }

    /// <summary>裸 stream_event 无引号包裹时返回 false（需带引号的 "stream_event"）。</summary>
    [Fact]
    public void IsStreamEvent_UnquotedStreamEvent_ReturnsFalse() {
        V1ReplBridgeTransport.IsStreamEvent("{\"type\":stream_event}")
            .Should().BeFalse();
    }

    /// <summary>嵌套 JSON 中含 type 与 stream_event 仍返回 true（实现为字符串包含，非 JSON 解析）。</summary>
    [Fact]
    public void IsStreamEvent_NestedWithBothTokens_ReturnsTrue() {
        V1ReplBridgeTransport.IsStreamEvent("{\"meta\":{\"type\":\"x\"},\"payload\":\"stream_event\"}")
            .Should().BeTrue();
    }

    // === IsPermanentCloseCode: 永久关闭码 1002/4001 ===

    /// <summary>1002 返回 true（永久关闭）。</summary>
    [Fact]
    public void IsPermanentCloseCode_1002_ReturnsTrue() {
        V1ReplBridgeTransport.IsPermanentCloseCode(1002).Should().BeTrue();
    }

    /// <summary>4001 返回 true（永久关闭）。</summary>
    [Fact]
    public void IsPermanentCloseCode_4001_ReturnsTrue() {
        V1ReplBridgeTransport.IsPermanentCloseCode(4001).Should().BeTrue();
    }

    /// <summary>4003 返回 false（非永久关闭，走 4003 刷新路径）。</summary>
    [Fact]
    public void IsPermanentCloseCode_4003_ReturnsFalse() {
        V1ReplBridgeTransport.IsPermanentCloseCode(4003).Should().BeFalse();
    }

    /// <summary>1000/1001 等正常关闭码返回 false。</summary>
    [Fact]
    public void IsPermanentCloseCode_NormalCloseCodes_ReturnsFalse() {
        V1ReplBridgeTransport.IsPermanentCloseCode(1000).Should().BeFalse();
        V1ReplBridgeTransport.IsPermanentCloseCode(1001).Should().BeFalse();
        V1ReplBridgeTransport.IsPermanentCloseCode(1006).Should().BeFalse();
    }

    /// <summary>null 返回 false（无关闭码可重连）。</summary>
    [Fact]
    public void IsPermanentCloseCode_Null_ReturnsFalse() {
        V1ReplBridgeTransport.IsPermanentCloseCode(null).Should().BeFalse();
    }

    // === ShouldRetryOn4003: 4003 且 headers 已变化 ===

    /// <summary>4003 且 freshHeader 与 authHeader 不同时返回 true。</summary>
    [Fact]
    public void ShouldRetryOn4003_HeadersChanged_ReturnsTrue() {
        V1ReplBridgeTransport.ShouldRetryOn4003(4003, "new-auth", "old-auth").Should().BeTrue();
    }

    /// <summary>4003 且 freshHeader 与 authHeader 相同时返回 false。</summary>
    [Fact]
    public void ShouldRetryOn4003_HeadersSame_ReturnsFalse() {
        V1ReplBridgeTransport.ShouldRetryOn4003(4003, "same-auth", "same-auth").Should().BeFalse();
    }

    /// <summary>非 4003 关闭码返回 false（即使 headers 不同）。</summary>
    [Fact]
    public void ShouldRetryOn4003_Non4003CloseCode_ReturnsFalse() {
        V1ReplBridgeTransport.ShouldRetryOn4003(1002, "new", "old").Should().BeFalse();
        V1ReplBridgeTransport.ShouldRetryOn4003(1006, "new", "old").Should().BeFalse();
    }

    /// <summary>4003 且两者均为 null 时返回 false（未变化）。</summary>
    [Fact]
    public void ShouldRetryOn4003_BothNull_ReturnsFalse() {
        V1ReplBridgeTransport.ShouldRetryOn4003(4003, null, null).Should().BeFalse();
    }

    /// <summary>4003 且 freshHeader 为 null、authHeader 非 null 时返回 true（已变化）。</summary>
    [Fact]
    public void ShouldRetryOn4003_FreshNullAuthNonNull_ReturnsTrue() {
        V1ReplBridgeTransport.ShouldRetryOn4003(4003, null, "old").Should().BeTrue();
    }

    // === ShouldResetReconnectBudget: 休眠检测 ===

    /// <summary>上次重连距今超过阈值时返回 true（检测到休眠）。</summary>
    [Fact]
    public void ShouldResetReconnectBudget_ExceedsThreshold_ReturnsTrue() {
        // last=1000, now=70000, threshold=60000 → 69000 > 60000
        V1ReplBridgeTransport.ShouldResetReconnectBudget(1000, 70000, 60000).Should().BeTrue();
    }

    /// <summary>上次重连距今等于阈值时返回 false（严格大于）。</summary>
    [Fact]
    public void ShouldResetReconnectBudget_EqualsThreshold_ReturnsFalse() {
        // last=1000, now=61000, threshold=60000 → 60000 == 60000
        V1ReplBridgeTransport.ShouldResetReconnectBudget(1000, 61000, 60000).Should().BeFalse();
    }

    /// <summary>上次重连距今小于阈值时返回 false。</summary>
    [Fact]
    public void ShouldResetReconnectBudget_BelowThreshold_ReturnsFalse() {
        V1ReplBridgeTransport.ShouldResetReconnectBudget(1000, 50000, 60000).Should().BeFalse();
    }

    /// <summary>lastReconnectAttemptTime=0（从未重连）时返回 false。</summary>
    [Fact]
    public void ShouldResetReconnectBudget_NeverReconnected_ReturnsFalse() {
        V1ReplBridgeTransport.ShouldResetReconnectBudget(0, 999999, 60000).Should().BeFalse();
    }

    /// <summary>now 小于 last（时钟回拨）时返回 false（差为负，不大于正阈值）。</summary>
    [Fact]
    public void ShouldResetReconnectBudget_ClockBackward_ReturnsFalse() {
        V1ReplBridgeTransport.ShouldResetReconnectBudget(10000, 5000, 60000).Should().BeFalse();
    }

    // === IsReconnectBudgetExhausted: 预算耗尽 ===

    /// <summary>已过时间超过放弃阈值时返回 true。</summary>
    [Fact]
    public void IsReconnectBudgetExhausted_ExceedsGiveUp_ReturnsTrue() {
        V1ReplBridgeTransport.IsReconnectBudgetExhausted(600_001, 600_000).Should().BeTrue();
    }

    /// <summary>已过时间等于放弃阈值时返回 true（大于等于）。</summary>
    [Fact]
    public void IsReconnectBudgetExhausted_EqualsGiveUp_ReturnsTrue() {
        V1ReplBridgeTransport.IsReconnectBudgetExhausted(600_000, 600_000).Should().BeTrue();
    }

    /// <summary>已过时间小于放弃阈值时返回 false。</summary>
    [Fact]
    public void IsReconnectBudgetExhausted_BelowGiveUp_ReturnsFalse() {
        V1ReplBridgeTransport.IsReconnectBudgetExhausted(599_999, 600_000).Should().BeFalse();
    }

    /// <summary>已过时间为 0 时返回 false。</summary>
    [Fact]
    public void IsReconnectBudgetExhausted_ZeroElapsed_ReturnsFalse() {
        V1ReplBridgeTransport.IsReconnectBudgetExhausted(0, 600_000).Should().BeFalse();
    }

    // === ComputeReconnectBaseDelay: 指数退避（不含抖动） ===

    /// <summary>第 1 次重连返回 baseDelay（2^0=1）。</summary>
    [Fact]
    public void ComputeReconnectBaseDelay_FirstAttempt_ReturnsBaseDelay() {
        V1ReplBridgeTransport.ComputeReconnectBaseDelay(1, 1000, 30000).Should().Be(1000);
    }

    /// <summary>第 2 次重连返回 baseDelay*2（2^1=2）。</summary>
    [Fact]
    public void ComputeReconnectBaseDelay_SecondAttempt_ReturnsDoubleBase() {
        V1ReplBridgeTransport.ComputeReconnectBaseDelay(2, 1000, 30000).Should().Be(2000);
    }

    /// <summary>第 4 次重连返回 baseDelay*8（2^3=8）。</summary>
    [Fact]
    public void ComputeReconnectBaseDelay_FourthAttempt_ReturnsEightTimesBase() {
        V1ReplBridgeTransport.ComputeReconnectBaseDelay(4, 1000, 30000).Should().Be(8000);
    }

    /// <summary>指数退避被 MaxDelayMs 钳制。</summary>
    [Fact]
    public void ComputeReconnectBaseDelay_ExceedsMax_ClampedToMax() {
        // 2^14 * 1000 = 16384000 > 30000 → 30000
        V1ReplBridgeTransport.ComputeReconnectBaseDelay(15, 1000, 30000).Should().Be(30000);
    }

    /// <summary>指数上限 10 防止位移溢出（attempts-1 > 10 时按 2^10 计算）。</summary>
    [Fact]
    public void ComputeReconnectBaseDelay_AttemptsBeyondTen_CappedAtTwoToTen() {
        // attempts=20: 2^min(19,10)=2^10=1024, 1024*1000=1024000 > 30000 → 30000
        V1ReplBridgeTransport.ComputeReconnectBaseDelay(20, 1000, 30000).Should().Be(30000);
        // 用大 maxDelay 验证指数确实被钳在 2^10
        V1ReplBridgeTransport.ComputeReconnectBaseDelay(20, 1000, 10_000_000).Should().Be(1024 * 1000);
    }

    /// <summary>第 11 次重连恰好 2^10（指数上限边界）。</summary>
    [Fact]
    public void ComputeReconnectBaseDelay_EleventhAttempt_HitsExponentCap() {
        // attempts=11: 2^min(10,10)=2^10=1024
        V1ReplBridgeTransport.ComputeReconnectBaseDelay(11, 1000, 10_000_000).Should().Be(1024 * 1000);
    }
}
