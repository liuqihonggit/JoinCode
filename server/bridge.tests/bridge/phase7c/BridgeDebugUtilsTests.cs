
namespace Bridge.Tests.Phase7C;

public sealed class BridgeDebugUtilsTests
{
    [Fact]
    public void RedactSecrets_AccessToken_Redacted()
    {
        var input = """{"access_token":"abcdefghijklmnop","other":"value"}""";
        var result = BridgeDebugUtils.RedactSecrets(input);
        Assert.Contains("abcdefgh...mnop", result);
        Assert.DoesNotContain("abcdefghijklmnop", result);
    }

    [Fact]
    public void RedactSecrets_ShortToken_Redacted()
    {
        var input = """{"token":"short"}""";
        var result = BridgeDebugUtils.RedactSecrets(input);
        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain("short", result);
    }

    [Fact]
    public void RedactSecrets_EnvironmentSecret_Redacted()
    {
        var input = """{"environment_secret":"1234567890abcdef12345678"}""";
        var result = BridgeDebugUtils.RedactSecrets(input);
        // 长度>=16 保留前8后4
        Assert.Contains("12345678...5678", result);
        Assert.DoesNotContain("1234567890abcdef12345678", result);
    }

    [Fact]
    public void RedactSecrets_NoSecrets_ReturnsAsIs()
    {
        var input = """{"name":"test","count":42}""";
        var result = BridgeDebugUtils.RedactSecrets(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void DebugTruncate_ShortString_ReturnsAsIs()
    {
        var input = "short string";
        var result = BridgeDebugUtils.DebugTruncate(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void DebugTruncate_LongString_Truncates()
    {
        var input = new string('a', 3000);
        var result = BridgeDebugUtils.DebugTruncate(input);
        Assert.True(result.Length < 3000);
        Assert.Contains("truncated", result);
    }

    [Fact]
    public void DebugBody_JsonElement_SerializesAndTruncates()
    {
        var json = """{"key":"value"}""";
        var je = JsonDocument.Parse(json).RootElement;
        var result = BridgeDebugUtils.DebugBody(je);
        Assert.Contains("key", result);
    }

    [Fact]
    public void DescribeHttpError_HttpRequestException_ReturnsMessage()
    {
        var ex = new HttpRequestException("test error");
        var result = BridgeDebugUtils.DescribeHttpError(ex);
        Assert.Equal("test error", result);
    }

    [Fact]
    public void ExtractHttpStatus_HttpRequestExceptionWithStatus_ReturnsStatus()
    {
        var ex = new HttpRequestException("error", null, System.Net.HttpStatusCode.NotFound);
        var result = BridgeDebugUtils.ExtractHttpStatus(ex);
        Assert.Equal(404, result);
    }

    [Fact]
    public void ExtractHttpStatus_NonHttpException_ReturnsNull()
    {
        var ex = new InvalidOperationException("error");
        var result = BridgeDebugUtils.ExtractHttpStatus(ex);
        Assert.Null(result);
    }

    [Fact]
    public void ExtractErrorDetail_WithMessage_ReturnsMessage()
    {
        var json = """{"message":"error detail"}""";
        var je = JsonDocument.Parse(json).RootElement;
        var result = BridgeDebugUtils.ExtractErrorDetail(je);
        Assert.Equal("error detail", result);
    }

    [Fact]
    public void ExtractErrorDetail_WithNestedError_ReturnsNestedMessage()
    {
        var json = """{"error":{"message":"nested error"}}""";
        var je = JsonDocument.Parse(json).RootElement;
        var result = BridgeDebugUtils.ExtractErrorDetail(je);
        Assert.Equal("nested error", result);
    }

    [Fact]
    public void ExtractErrorDetail_NullElement_ReturnsNull()
    {
        var result = BridgeDebugUtils.ExtractErrorDetail(null);
        Assert.Null(result);
    }
}
