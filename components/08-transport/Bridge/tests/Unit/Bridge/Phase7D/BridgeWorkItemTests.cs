
namespace Bridge.Tests.Phase7D;

public sealed partial class BridgeMainTests
{
    #region BridgeCreateSessionRequest — 模型测试

    [Fact]
    public void BridgeCreateSessionRequest_RequiredFields()
    {
        var req = new BridgeCreateSessionRequest
        {
            EnvironmentId = "env-123",
        };
        Assert.Equal("env-123", req.EnvironmentId);
        Assert.Null(req.Title);
        Assert.Null(req.GitRepoUrl);
        Assert.Null(req.Branch);
        Assert.Null(req.PermissionMode);
    }

    [Fact]
    public void BridgeCreateSessionRequest_AllFields()
    {
        var req = new BridgeCreateSessionRequest
        {
            EnvironmentId = "env-456",
            Title = "test-session",
            GitRepoUrl = "https://github.com/test/repo",
            Branch = "main",
            PermissionMode = "auto-accept",
        };
        Assert.Equal("env-456", req.EnvironmentId);
        Assert.Equal("test-session", req.Title);
        Assert.Equal("https://github.com/test/repo", req.GitRepoUrl);
        Assert.Equal("main", req.Branch);
        Assert.Equal("auto-accept", req.PermissionMode);
    }

    #endregion

    #region BridgeWorkItem — WorkType 字段测试

    [Fact]
    public void BridgeWorkItem_WorkType_DefaultNull()
    {
        var item = new BridgeWorkItem
        {
            WorkId = "w1",
            SessionId = "s1",
        };
        Assert.Null(item.WorkType);
    }

    [Fact]
    public void BridgeWorkItem_WorkType_Healthcheck()
    {
        var item = new BridgeWorkItem
        {
            WorkId = "w1",
            SessionId = "s1",
            WorkType = "healthcheck",
        };
        Assert.Equal("healthcheck", item.WorkType);
    }

    [Fact]
    public void BridgeWorkItem_WorkType_Session()
    {
        var item = new BridgeWorkItem
        {
            WorkId = "w2",
            SessionId = "s2",
            WorkType = "session",
        };
        Assert.Equal("session", item.WorkType);
    }

    [Fact]
    public void BridgeWorkItem_Secret_Field()
    {
        var item = new BridgeWorkItem
        {
            WorkId = "w3",
            SessionId = "s3",
            Secret = "dGVzdA",
        };
        Assert.Equal("dGVzdA", item.Secret);
    }

    #endregion

    #region BridgeWorkSecretDecoder — 解码测试

    [Fact]
    public void DecodeWorkSecret_ValidSecret_ReturnsParsed()
    {
        var json = "{\"version\":1,\"session_ingress_token\":\"jwt-token-123\",\"api_base_url\":\"https://api.test.com\"}";
        var base64url = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var result = BridgeWorkSecretDecoder.DecodeWorkSecret(base64url);

        Assert.Equal(1, result.Version);
        Assert.Equal("jwt-token-123", result.SessionIngressToken);
        Assert.Equal("https://api.test.com", result.ApiBaseUrl);
        Assert.False(result.UseCodeSessions);
    }

    [Fact]
    public void DecodeWorkSecret_WithUseCodeSessions_ReturnsTrue()
    {
        var json = "{\"version\":1,\"session_ingress_token\":\"jwt-token-456\",\"api_base_url\":\"https://api.test.com\",\"use_code_sessions\":true}";
        var base64url = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var result = BridgeWorkSecretDecoder.DecodeWorkSecret(base64url);

        Assert.True(result.UseCodeSessions);
    }

    [Fact]
    public void DecodeWorkSecret_InvalidVersion_Throws()
    {
        var json = "{\"version\":2,\"session_ingress_token\":\"jwt\",\"api_base_url\":\"https://api.test.com\"}";
        var base64url = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        Assert.Throws<InvalidOperationException>(() => BridgeWorkSecretDecoder.DecodeWorkSecret(base64url));
    }

    [Fact]
    public void DecodeWorkSecret_MissingToken_Throws()
    {
        var json = "{\"version\":1,\"api_base_url\":\"https://api.test.com\"}";
        var base64url = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        Assert.Throws<System.Text.Json.JsonException>(() => BridgeWorkSecretDecoder.DecodeWorkSecret(base64url));
    }

    [Fact]
    public void DecodeWorkSecret_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BridgeWorkSecretDecoder.DecodeWorkSecret(null!));
    }

    [Fact]
    public void DecodeWorkSecret_WithEnvironmentVariables()
    {
        var json = "{\"version\":1,\"session_ingress_token\":\"jwt-789\",\"api_base_url\":\"https://api.test.com\",\"environment_variables\":{\"KEY1\":\"val1\",\"KEY2\":\"val2\"}}";
        var base64url = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var result = BridgeWorkSecretDecoder.DecodeWorkSecret(base64url);

        Assert.NotNull(result.EnvironmentVariables);
        Assert.Equal(2, result.EnvironmentVariables.Count);
        Assert.Equal("val1", result.EnvironmentVariables["KEY1"]);
    }

    #endregion
}
