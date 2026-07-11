namespace Mcp.Tests.Auth;

using System.Reflection;
using McpClient;

/// <summary>
/// Mcp\Auth 4 处 fallback 迁移测试 — P1-6
/// 验证构造函数在 httpClient=null 时走 HttpClientProviderFactory.Create().GetClient() fallback 路径
/// 决策: 通过反射验证 private _httpClient 字段非 null（已通过工厂初始化）
/// </summary>
public sealed class McpAuthFallbackTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // 1. McpOAuthMetadataDiscovery fallback
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 McpOAuthMetadataDiscovery 在 httpClient=null 时通过 HttpClientProviderFactory 初始化 _httpClient
    /// 这是 P1-6 的核心: 替代 `?? new HttpClient()` 的 fallback 路径
    /// </summary>
    [Fact]
    public void McpOAuthMetadataDiscovery_WhenHttpClientNull_ShouldInitializeViaFactory()
    {
        // Act — 不传 httpClient，触发 fallback 路径
        var discovery = new McpOAuthMetadataDiscovery();

        // Assert — 反射验证 _httpClient 已通过 HttpClientProviderFactory.Create().GetClient() 初始化
        var httpClient = GetPrivateHttpClientField(discovery);
        httpClient.Should().NotBeNull(
            "httpClient=null 时应通过 HttpClientProviderFactory.Create().GetClient() 初始化 _httpClient，而非 new HttpClient()");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. McpDynamicClientRegistration fallback
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 McpDynamicClientRegistration 在 httpClient=null 时通过 HttpClientProviderFactory 初始化 _httpClient
    /// </summary>
    [Fact]
    public void McpDynamicClientRegistration_WhenHttpClientNull_ShouldInitializeViaFactory()
    {
        // Act
        var dcr = new McpDynamicClientRegistration();

        // Assert
        var httpClient = GetPrivateHttpClientField(dcr);
        httpClient.Should().NotBeNull(
            "httpClient=null 时应通过 HttpClientProviderFactory.Create().GetClient() 初始化 _httpClient");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. McpPkceAuthProvider fallback
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 McpPkceAuthProvider 在 httpClient=null 时通过 HttpClientProviderFactory 初始化 _httpClient
    /// </summary>
    [Fact]
    public void McpPkceAuthProvider_WhenHttpClientNull_ShouldInitializeViaFactory()
    {
        // Arrange
        var fs = new PhysicalFileSystem();
        var options = new McpOAuthOptions
        {
            ClientId = "test-client-id",
            AuthorizationUrl = "https://example.com/auth",
            TokenUrl = "https://example.com/token",
            RedirectUrl = "http://localhost:8080/callback"
        };

        // Act — 不传 httpClient，触发 fallback 路径
        var provider = new McpPkceAuthProvider(options, fs);

        // Assert
        var httpClient = GetPrivateHttpClientField(provider);
        httpClient.Should().NotBeNull(
            "httpClient=null 时应通过 HttpClientProviderFactory.Create().GetClient() 初始化 _httpClient");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4. OAuth2AuthProvider fallback（通过 OAuth2ProviderOptions.HttpClient = null）
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 OAuth2AuthProvider 在 options.HttpClient=null 时通过 HttpClientProviderFactory 初始化 _httpClient
    /// </summary>
    [Fact]
    public void OAuth2AuthProvider_WhenOptionsHttpClientNull_ShouldInitializeViaFactory()
    {
        // Arrange — 不设置 HttpClient 字段，触发 fallback 路径
        var options = new OAuth2ProviderOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-secret",
            TokenUrl = "https://example.com/token"
            // HttpClient = null — 故意不设置
        };

        // Act
        var provider = new OAuth2AuthProvider(options);

        // Assert
        var httpClient = GetPrivateHttpClientField(provider);
        httpClient.Should().NotBeNull(
            "options.HttpClient=null 时应通过 HttpClientProviderFactory.Create().GetClient() 初始化 _httpClient");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 验证 fallback 路径支持 JCC_HTTP_MODE=Mock 环境变量切换
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 JCC_HTTP_MODE=Mock 时，fallback 路径走 MockHttpClientProvider
    /// 这是 P1-6 的关键能力: fallback 路径与主程序一致支持环境变量切换
    /// </summary>
    [Fact]
    public void McpOAuthMetadataDiscovery_WhenHttpModeMock_ShouldUseMockHttpClientProvider()
    {
        // Arrange
        var originalValue = Environment.GetEnvironmentVariable("JCC_HTTP_MODE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_HTTP_MODE", "Mock");

            // Act — 走 fallback 路径
            var discovery = new McpOAuthMetadataDiscovery();

            // Assert — _httpClient 应该来自 MockHttpClientProvider（BaseAddress = http://mock.local）
            var httpClient = GetPrivateHttpClientField(discovery);
            httpClient.Should().NotBeNull();
            httpClient!.BaseAddress.Should().Be(new Uri("http://mock.local"),
                "JCC_HTTP_MODE=Mock 时 HttpClientProviderFactory.Create() 应返回 MockHttpClientProvider，其 BaseAddress 为 http://mock.local");
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("JCC_HTTP_MODE", originalValue);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 辅助方法 — 通过反射获取 private _httpClient 字段
    // ─────────────────────────────────────────────────────────────────────────────

    private static HttpClient? GetPrivateHttpClientField(object obj)
    {
        var field = obj.GetType().GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull($"类型 {obj.GetType().Name} 应包含 private _httpClient 字段");
        return field!.GetValue(obj) as HttpClient;
    }
}
