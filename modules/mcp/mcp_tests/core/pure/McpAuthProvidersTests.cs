namespace Mcp.Tests;

/// <summary>
/// McpAuthProviders 单元测试 — 验证认证 Provider 工厂分派 + ApiKey/Bearer/Basic 头构造
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class McpAuthProvidersTests {
    [Fact]
    public async Task ApiKey_GetAuthHeadersAsync_ReturnsHeaderWithApiKey() {
        var provider = new ApiKeyAuthProvider("my-secret-key");
        var headers = await provider.GetAuthHeadersAsync();
        headers.Should().HaveCount(1);
        headers["X-API-Key"].Should().Be("my-secret-key");
    }

    [Fact]
    public async Task ApiKey_GetAuthHeadersAsync_CustomHeaderName() {
        var provider = new ApiKeyAuthProvider("key123", "X-Custom-Auth");
        var headers = await provider.GetAuthHeadersAsync();
        headers["X-Custom-Auth"].Should().Be("key123");
        headers.Should().NotContainKey("X-API-Key");
    }

    [Fact]
    public void ApiKey_AuthType_IsApiKey() {
        var provider = new ApiKeyAuthProvider("k");
        provider.AuthType.Should().Be(McpAuthType.ApiKey);
    }

    [Fact]
    public void ApiKey_IsAuthenticated_NonEmptyKey_True() {
        new ApiKeyAuthProvider("k").IsAuthenticated.Should().BeTrue();
        new ApiKeyAuthProvider("").IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Bearer_GetAuthHeadersAsync_ReturnsAuthorizationBearer() {
        var provider = new BearerAuthProvider("my-token");
        var headers = await provider.GetAuthHeadersAsync();
        headers.Should().HaveCount(1);
        headers["Authorization"].Should().Be("Bearer my-token");
    }

    [Fact]
    public void Bearer_AuthType_IsBearer() {
        new BearerAuthProvider("t").AuthType.Should().Be(McpAuthType.Bearer);
    }

    [Fact]
    public void Bearer_IsAuthenticated_NonEmptyToken_True() {
        new BearerAuthProvider("t").IsAuthenticated.Should().BeTrue();
        new BearerAuthProvider("").IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Basic_GetAuthHeadersAsync_ReturnsAuthorizationBasicBase64() {
        var provider = new BasicAuthProvider("user", "pass");
        var headers = await provider.GetAuthHeadersAsync();
        headers.Should().HaveCount(1);
        var expectedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
        headers["Authorization"].Should().Be($"Basic {expectedCredentials}");
    }

    [Fact]
    public async Task Basic_GetAuthHeadersAsync_CorrectBase64Encoding() {
        var provider = new BasicAuthProvider("admin", "secret123");
        var headers = await provider.GetAuthHeadersAsync();
        var expectedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret123"));
        headers["Authorization"].Should().Be($"Basic {expectedCredentials}");
    }

    [Fact]
    public void Basic_AuthType_IsBasic() {
        new BasicAuthProvider("u", "p").AuthType.Should().Be(McpAuthType.Basic);
    }

    [Fact]
    public void Basic_IsAuthenticated_BothNonEmpty_True() {
        new BasicAuthProvider("u", "p").IsAuthenticated.Should().BeTrue();
        new BasicAuthProvider("", "p").IsAuthenticated.Should().BeFalse();
        new BasicAuthProvider("u", "").IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Factory_Create_ApiKey_ReturnsApiKeyProvider() {
        var config = new McpAuthConfig { Type = McpAuthType.ApiKey, ApiKey = "test-key" };
        var provider = McpAuthProviderFactory.Create(config);
        provider.Should().BeOfType<ApiKeyAuthProvider>();
        provider.AuthType.Should().Be(McpAuthType.ApiKey);
    }

    [Fact]
    public void Factory_Create_Bearer_ReturnsBearerProvider() {
        var config = new McpAuthConfig { Type = McpAuthType.Bearer, BearerToken = "tok" };
        var provider = McpAuthProviderFactory.Create(config);
        provider.Should().BeOfType<BearerAuthProvider>();
        provider.AuthType.Should().Be(McpAuthType.Bearer);
    }

    [Fact]
    public void Factory_Create_Basic_ReturnsBasicProvider() {
        var config = new McpAuthConfig { Type = McpAuthType.Basic, Username = "u", Password = "p" };
        var provider = McpAuthProviderFactory.Create(config);
        provider.Should().BeOfType<BasicAuthProvider>();
        provider.AuthType.Should().Be(McpAuthType.Basic);
    }

    [Fact]
    public void Factory_Create_OAuth2_ReturnsOAuth2Provider() {
        var config = new McpAuthConfig {
            Type = McpAuthType.OAuth2,
            ClientId = "cid",
            ClientSecret = "csecret",
            TokenUrl = "https://example.com/token"
        };
        var provider = McpAuthProviderFactory.Create(config);
        provider.Should().BeOfType<OAuth2AuthProvider>();
        provider.AuthType.Should().Be(McpAuthType.OAuth2);
    }

    [Fact]
    public void Factory_Create_ApiKey_NullKey_Throws() {
        var config = new McpAuthConfig { Type = McpAuthType.ApiKey, ApiKey = null };
        var act = () => McpAuthProviderFactory.Create(config);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Factory_Create_Bearer_NullToken_Throws() {
        var config = new McpAuthConfig { Type = McpAuthType.Bearer, BearerToken = null };
        var act = () => McpAuthProviderFactory.Create(config);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Factory_Create_Basic_NullUsername_Throws() {
        var config = new McpAuthConfig { Type = McpAuthType.Basic, Username = null, Password = "p" };
        var act = () => McpAuthProviderFactory.Create(config);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Factory_Create_Basic_NullPassword_Throws() {
        var config = new McpAuthConfig { Type = McpAuthType.Basic, Username = "u", Password = null };
        var act = () => McpAuthProviderFactory.Create(config);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Factory_Create_OAuth2_NullClientId_Throws() {
        var config = new McpAuthConfig {
            Type = McpAuthType.OAuth2,
            ClientId = null,
            ClientSecret = "cs",
            TokenUrl = "https://example.com/token"
        };
        var act = () => McpAuthProviderFactory.Create(config);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Factory_Create_UnsupportedType_Throws() {
        var config = new McpAuthConfig { Type = McpAuthType.None };
        var act = () => McpAuthProviderFactory.Create(config);
        act.Should().Throw<NotSupportedException>();
    }
}
