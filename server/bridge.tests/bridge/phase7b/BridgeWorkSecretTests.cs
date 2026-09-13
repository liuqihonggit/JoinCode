
namespace Bridge.Tests.Phase7B;

public sealed class BridgeWorkSecretTests
{
    [Fact]
    public void DecodeWorkSecret_ValidBase64Url_ReturnsSecret()
    {
        var json = """{"version":1,"session_ingress_token":"tok123","api_base_url":"https://api.example.com","sources":[{"type":"git","git_info":{"repo":"https://github.com/test","ref":"main"}}]}""";
        var base64Url = ToBase64UrlString(System.Text.Encoding.UTF8.GetBytes(json));

        var result = BridgeWorkSecretDecoder.DecodeWorkSecret(base64Url);
        Assert.NotNull(result);
        Assert.Equal("tok123", result.SessionIngressToken);
        Assert.Equal("https://api.example.com", result.ApiBaseUrl);
        Assert.NotNull(result.Sources);
        Assert.Single(result.Sources);
        Assert.NotNull(result.Sources[0].GitInfo);
        Assert.Equal("https://github.com/test", result.Sources[0].GitInfo!.Repo);
        Assert.Equal("main", result.Sources[0].GitInfo!.Ref);
    }

    [Fact]
    public void DecodeWorkSecret_InvalidBase64_ThrowsException()
    {
        Assert.ThrowsAny<Exception>(() => BridgeWorkSecretDecoder.DecodeWorkSecret("not-valid-base64!!!"));
    }

    [Fact]
    public void BuildSdkUrl_ReturnsCorrectUrl()
    {
        var url = BridgeWorkSecretDecoder.BuildSdkUrl("https://api.example.com", "env123");
        Assert.Contains("env123", url);
        Assert.Contains("api.example.com", url);
    }

    [Fact]
    public void BuildCCRv2SdkUrl_ReturnsCorrectUrl()
    {
        var url = BridgeWorkSecretDecoder.BuildCCRv2SdkUrl("https://api2.example.com", "env456");
        Assert.Contains("env456", url);
    }

    private static string ToBase64UrlString(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
