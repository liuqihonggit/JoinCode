namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// V2ReplBridgeTransport 确定性测试 — IsSsePermanentRejection 永久拒绝码 + TryParseSequenceNumber 序列号解析纯函数。
/// 不建立 SSE 连接，仅测纯计算分支。
/// </summary>
public class V2ReplBridgeTransportTest {
    // === IsSsePermanentRejection: 401/403/404 永久拒绝 ===

    /// <summary>401 Unauthorized 返回 true。</summary>
    [Fact]
    public void IsSsePermanentRejection_Unauthorized_ReturnsTrue() {
        V2ReplBridgeTransport.IsSsePermanentRejection(System.Net.HttpStatusCode.Unauthorized).Should().BeTrue();
    }

    /// <summary>403 Forbidden 返回 true。</summary>
    [Fact]
    public void IsSsePermanentRejection_Forbidden_ReturnsTrue() {
        V2ReplBridgeTransport.IsSsePermanentRejection(System.Net.HttpStatusCode.Forbidden).Should().BeTrue();
    }

    /// <summary>404 NotFound 返回 true。</summary>
    [Fact]
    public void IsSsePermanentRejection_NotFound_ReturnsTrue() {
        V2ReplBridgeTransport.IsSsePermanentRejection(System.Net.HttpStatusCode.NotFound).Should().BeTrue();
    }

    /// <summary>200 OK 返回 false（正常响应）。</summary>
    [Fact]
    public void IsSsePermanentRejection_Ok_ReturnsFalse() {
        V2ReplBridgeTransport.IsSsePermanentRejection(System.Net.HttpStatusCode.OK).Should().BeFalse();
    }

    /// <summary>500 InternalServerError 返回 false（5xx 可重试非永久拒绝）。</summary>
    [Fact]
    public void IsSsePermanentRejection_ServerError_ReturnsFalse() {
        V2ReplBridgeTransport.IsSsePermanentRejection(System.Net.HttpStatusCode.InternalServerError).Should().BeFalse();
        V2ReplBridgeTransport.IsSsePermanentRejection(System.Net.HttpStatusCode.BadGateway).Should().BeFalse();
        V2ReplBridgeTransport.IsSsePermanentRejection(System.Net.HttpStatusCode.ServiceUnavailable).Should().BeFalse();
    }

    /// <summary>429 TooManyRequests 返回 false（429 可重试非永久拒绝）。</summary>
    [Fact]
    public void IsSsePermanentRejection_TooManyRequests_ReturnsFalse() {
        V2ReplBridgeTransport.IsSsePermanentRejection(System.Net.HttpStatusCode.TooManyRequests).Should().BeFalse();
    }

    /// <summary>409 Conflict 返回 false（epoch 不匹配走另一路径）。</summary>
    [Fact]
    public void IsSsePermanentRejection_Conflict_ReturnsFalse() {
        V2ReplBridgeTransport.IsSsePermanentRejection(System.Net.HttpStatusCode.Conflict).Should().BeFalse();
    }

    // === TryParseSequenceNumber: SSE 事件 ID 解析 ===

    /// <summary>有效整数字符串解析成功。</summary>
    [Fact]
    public void TryParseSequenceNumber_ValidInteger_ReturnsTrueAndValue() {
        V2ReplBridgeTransport.TryParseSequenceNumber("42", out var seqNum).Should().BeTrue();
        seqNum.Should().Be(42);
    }

    /// <summary>0 解析成功。</summary>
    [Fact]
    public void TryParseSequenceNumber_Zero_ReturnsTrue() {
        V2ReplBridgeTransport.TryParseSequenceNumber("0", out var seqNum).Should().BeTrue();
        seqNum.Should().Be(0);
    }

    /// <summary>大整数解析成功。</summary>
    [Fact]
    public void TryParseSequenceNumber_LargeInteger_ReturnsTrue() {
        V2ReplBridgeTransport.TryParseSequenceNumber("2147483647", out var seqNum).Should().BeTrue();
        seqNum.Should().Be(int.MaxValue);
    }

    /// <summary>null 返回 false（无 ID）。</summary>
    [Fact]
    public void TryParseSequenceNumber_Null_ReturnsFalse() {
        V2ReplBridgeTransport.TryParseSequenceNumber(null, out var seqNum).Should().BeFalse();
        seqNum.Should().Be(0); // 失败时 out 参数为 0
    }

    /// <summary>空字符串返回 false。</summary>
    [Fact]
    public void TryParseSequenceNumber_EmptyString_ReturnsFalse() {
        V2ReplBridgeTransport.TryParseSequenceNumber(string.Empty, out var seqNum).Should().BeFalse();
        seqNum.Should().Be(0);
    }

    /// <summary>非数字字符串返回 false。</summary>
    [Fact]
    public void TryParseSequenceNumber_NonNumeric_ReturnsFalse() {
        V2ReplBridgeTransport.TryParseSequenceNumber("abc", out var seqNum).Should().BeFalse();
        seqNum.Should().Be(0);
    }

    /// <summary>带空格的数字返回 true（int.TryParse 默认 NumberStyles.Integer 允许前后空白）。</summary>
    [Fact]
    public void TryParseSequenceNumber_WithSpaces_ReturnsTrue() {
        V2ReplBridgeTransport.TryParseSequenceNumber(" 42 ", out var seqNum).Should().BeTrue();
        seqNum.Should().Be(42);
    }

    /// <summary>负数解析成功（int.TryParse 接受负号）。</summary>
    [Fact]
    public void TryParseSequenceNumber_Negative_ReturnsTrue() {
        V2ReplBridgeTransport.TryParseSequenceNumber("-1", out var seqNum).Should().BeTrue();
        seqNum.Should().Be(-1);
    }

    // === TryParseWorkerEpoch: registerWorker 响应解析（TASK071 #1 — 对齐 TS workSecret.ts:115-126）===
    // TS 契约: protojson 把 int64 序列化为字符串，Go 端也可能返回 number — 双形态都收；字段名是 worker_epoch。

    /// <summary>字符串形态（protojson int64）解析成功。</summary>
    [Fact]
    public void TryParseWorkerEpoch_StringForm_Parses() {
        V2ReplBridgeTransport.TryParseWorkerEpoch(@"{""worker_epoch"": ""42""}", out var epoch).Should().BeTrue();
        epoch.Should().Be(42);
    }

    /// <summary>数字形态解析成功。</summary>
    [Fact]
    public void TryParseWorkerEpoch_NumberForm_Parses() {
        V2ReplBridgeTransport.TryParseWorkerEpoch(@"{""worker_epoch"": 42}", out var epoch).Should().BeTrue();
        epoch.Should().Be(42);
    }

    /// <summary>缺失字段返回 false（调用方应 throw，禁止静默 0 — 旧 bug 恒 0 致 409 风暴）。</summary>
    [Fact]
    public void TryParseWorkerEpoch_MissingField_Fails() {
        V2ReplBridgeTransport.TryParseWorkerEpoch(@"{""ok"": true}", out var epoch).Should().BeFalse();
        epoch.Should().Be(0);
    }

    /// <summary>字段名错误（旧 bug 的 "epoch"）返回 false — 字段名回归锁（TS 契约 worker_epoch）。</summary>
    [Fact]
    public void TryParseWorkerEpoch_WrongFieldName_Fails() {
        V2ReplBridgeTransport.TryParseWorkerEpoch(@"{""epoch"": 42}", out var epoch).Should().BeFalse();
        epoch.Should().Be(0);
    }

    /// <summary>非数字字符串返回 false。</summary>
    [Fact]
    public void TryParseWorkerEpoch_NonNumericString_Fails() {
        V2ReplBridgeTransport.TryParseWorkerEpoch(@"{""worker_epoch"": ""abc""}", out var epoch).Should().BeFalse();
        epoch.Should().Be(0);
    }

    /// <summary>非法 JSON 返回 false。</summary>
    [Fact]
    public void TryParseWorkerEpoch_InvalidJson_Fails() {
        V2ReplBridgeTransport.TryParseWorkerEpoch("not-json", out var epoch).Should().BeFalse();
        epoch.Should().Be(0);
    }

    /// <summary>null 返回 false。</summary>
    [Fact]
    public void TryParseWorkerEpoch_Null_Fails() {
        V2ReplBridgeTransport.TryParseWorkerEpoch(null, out var epoch).Should().BeFalse();
        epoch.Should().Be(0);
    }
}
