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
}
