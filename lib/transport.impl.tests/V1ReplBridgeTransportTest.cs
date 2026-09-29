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
}
