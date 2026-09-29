namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// BridgeTokenRefreshScheduler.DecodeJwtExpiry 确定性测试 — JWT exp 声明解码，纯函数无时序依赖。
/// </summary>
public class BridgeTokenRefreshSchedulerTest {
    /// <summary>构造 base64url 编码的 JWT（header.payload.signature）。</summary>
    private static string MakeJwt(string payloadJson) {
        var header = Base64UrlEncode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode(payloadJson);
        return $"{header}.{payload}.signature";
    }

    /// <summary>UTF-8 字节 → base64url（去 padding，+→-，/→_）。</summary>
    private static string Base64UrlEncode(string s) {
        var bytes = Encoding.UTF8.GetBytes(s);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>含 exp 声明返回 exp * 1000（秒转毫秒）。</summary>
    [Fact]
    public void DecodeJwtExpiry_WithExp_ReturnsMilliseconds() {
        const long expSeconds = 1_700_000_000L;
        var jwt = MakeJwt($"{{\"exp\":{expSeconds}}}");

        var result = BridgeTokenRefreshScheduler.DecodeJwtExpiry(jwt);

        result.Should().Be(expSeconds * 1000L);
    }

    /// <summary>不含 exp 声明返回 null。</summary>
    [Fact]
    public void DecodeJwtExpiry_WithoutExp_ReturnsNull() {
        var jwt = MakeJwt("{\"sub\":\"user\",\"iat\":1700000000}");

        var result = BridgeTokenRefreshScheduler.DecodeJwtExpiry(jwt);

        result.Should().BeNull();
    }

    /// <summary>单段 token（无点分隔）返回 null。</summary>
    [Fact]
    public void DecodeJwtExpiry_SingleSegment_ReturnsNull() {
        var result = BridgeTokenRefreshScheduler.DecodeJwtExpiry("not-a-jwt");

        result.Should().BeNull();
    }

    /// <summary>payload 非合法 base64 返回 null（异常被吞）。</summary>
    [Fact]
    public void DecodeJwtExpiry_MalformedPayload_ReturnsNull() {
        var result = BridgeTokenRefreshScheduler.DecodeJwtExpiry("header.!!!not-base64!!!.sig");

        result.Should().BeNull();
    }

    /// <summary>payload 非合法 JSON 返回 null。</summary>
    [Fact]
    public void DecodeJwtExpiry_MalformedJson_ReturnsNull() {
        var badPayload = Base64UrlEncode("not-json");
        var jwt = $"header.{badPayload}.sig";

        var result = BridgeTokenRefreshScheduler.DecodeJwtExpiry(jwt);

        result.Should().BeNull();
    }

    /// <summary>exp 为非数字类型返回 null（GetInt64 抛异常被吞）。</summary>
    [Fact]
    public void DecodeJwtExpiry_ExpNonNumeric_ReturnsNull() {
        var jwt = MakeJwt("{\"exp\":\"not-a-number\"}");

        var result = BridgeTokenRefreshScheduler.DecodeJwtExpiry(jwt);

        result.Should().BeNull();
    }

    /// <summary>exp 为 0 返回 0（边界值）。</summary>
    [Fact]
    public void DecodeJwtExpiry_ExpZero_ReturnsZero() {
        var jwt = MakeJwt("{\"exp\":0}");

        var result = BridgeTokenRefreshScheduler.DecodeJwtExpiry(jwt);

        result.Should().Be(0L);
    }

    /// <summary>含 exp 与其他字段共存时仍正确解码 exp。</summary>
    [Fact]
    public void DecodeJwtExpiry_ExpWithOtherClaims_ReturnsExpMilliseconds() {
        const long expSeconds = 1_700_000_000L;
        var jwt = MakeJwt($"{{\"sub\":\"user\",\"exp\":{expSeconds},\"iat\":1699999999}}");

        var result = BridgeTokenRefreshScheduler.DecodeJwtExpiry(jwt);

        result.Should().Be(expSeconds * 1000L);
    }

    // === ClampExpiryToMilliseconds: long 溢出钳制 ===

    /// <summary>正常范围内 expSeconds*1000 不溢出。</summary>
    [Fact]
    public void ClampExpiryToMilliseconds_NormalRange_ReturnsProduct() {
        BridgeTokenRefreshScheduler.ClampExpiryToMilliseconds(1_700_000_000L).Should().Be(1_700_000_000_000L);
        BridgeTokenRefreshScheduler.ClampExpiryToMilliseconds(0L).Should().Be(0L);
        BridgeTokenRefreshScheduler.ClampExpiryToMilliseconds(1L).Should().Be(1000L);
    }

    /// <summary>expSeconds 超过 long.MaxValue/1000 时钳制到 long.MaxValue（防溢出）。</summary>
    [Fact]
    public void ClampExpiryToMilliseconds_OverflowPositive_ClampedToMaxValue() {
        var overSafe = long.MaxValue / 1000 + 1;
        BridgeTokenRefreshScheduler.ClampExpiryToMilliseconds(overSafe).Should().Be(long.MaxValue);
        BridgeTokenRefreshScheduler.ClampExpiryToMilliseconds(long.MaxValue).Should().Be(long.MaxValue);
    }

    /// <summary>expSeconds 小于 long.MinValue/1000 时钳制到 long.MinValue（防下溢）。</summary>
    [Fact]
    public void ClampExpiryToMilliseconds_UnderflowNegative_ClampedToMinValue() {
        var underSafe = long.MinValue / 1000 - 1;
        BridgeTokenRefreshScheduler.ClampExpiryToMilliseconds(underSafe).Should().Be(long.MinValue);
        BridgeTokenRefreshScheduler.ClampExpiryToMilliseconds(long.MinValue).Should().Be(long.MinValue);
    }

    /// <summary>expSeconds 恰为 long.MaxValue/1000 时不溢出（边界）。</summary>
    [Fact]
    public void ClampExpiryToMilliseconds_ExactlyMaxSafe_NoOverflow() {
        var maxSafe = long.MaxValue / 1000;
        var expected = maxSafe * 1000;
        BridgeTokenRefreshScheduler.ClampExpiryToMilliseconds(maxSafe).Should().Be(expected);
    }

    /// <summary>DecodeJwtExpiry 解码超大 exp 时返回钳制值而非溢出负数。</summary>
    [Fact]
    public void DecodeJwtExpiry_HugeExp_ReturnsClampedNotOverflow() {
        var hugeExp = long.MaxValue; // 远超 long.MaxValue/1000
        var jwt = MakeJwt($"{{\"exp\":{hugeExp}}}");

        var result = BridgeTokenRefreshScheduler.DecodeJwtExpiry(jwt);

        result.Should().Be(long.MaxValue);
    }
}
