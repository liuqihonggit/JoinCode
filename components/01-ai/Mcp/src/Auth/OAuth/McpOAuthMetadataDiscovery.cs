namespace McpClient;

/// <summary>
/// OAuth 元数据发现服务 — 对齐 TS fetchAuthServerMetadata
/// RFC 9728: OAuth 2.0 Protected Resource Metadata
/// RFC 8414: OAuth 2.0 Authorization Server Metadata
/// </summary>
public sealed partial class McpOAuthMetadataDiscovery
{
    private readonly HttpClient _httpClient;
    [Inject] private readonly ILogger<McpOAuthMetadataDiscovery>? _logger;

    // RFC 9728: Protected Resource Metadata well-known 路径
    private const string PrmWellKnownPath = "/.well-known/oauth-protected-resource";

    // RFC 8414: Authorization Server Metadata well-known 路径
    private const string AsWellKnownPath = "/.well-known/oauth-authorization-server";

    public McpOAuthMetadataDiscovery(HttpClient? httpClient = null, ILogger<McpOAuthMetadataDiscovery>? logger = null)
    {
        // P1-6: fallback 走 HttpClientProviderFactory（支持 JCC_HTTP_MODE=Mock 切换，对齐主程序 IHttpClientProvider 抽象）
        _httpClient = httpClient ?? HttpClientProviderFactory.Create().GetClient();
        _logger = logger;
    }

    /// <summary>
    /// 发现 OAuth 授权服务器元数据 — 对齐 TS fetchAuthServerMetadata
    /// 优先级: 用户配置 URL > RFC 9728→8414 链式发现 > 回退路径
    /// </summary>
    public async Task<OAuthAuthorizationServerMetadata?> DiscoverAsync(
        string serverUrl,
        string? configuredMetadataUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverUrl);

        // 优先级1: 用户配置的元数据 URL
        if (!string.IsNullOrEmpty(configuredMetadataUrl))
        {
            _logger?.LogInformation("使用用户配置的元数据 URL: {Url}", configuredMetadataUrl);
            return await FetchMetadataFromUrlAsync(configuredMetadataUrl, cancellationToken).ConfigureAwait(false);
        }

        // 优先级2: RFC 9728 → RFC 8414 链式发现
        try
        {
            var result = await DiscoverViaRfc9728Async(serverUrl, cancellationToken).ConfigureAwait(false);
            if (result is not null) return result;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "RFC 9728 发现失败，尝试回退路径");
        }

        // 优先级3: 回退路径 — 直接对服务器 URL 执行 RFC 8414 发现
        return await DiscoverViaRfc8414Async(serverUrl, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 发现 Protected Resource Metadata — 对齐 TS discoverProtectedResource
    /// RFC 9728: GET /.well-known/oauth-protected-resource
    /// </summary>
    public async Task<OAuthProtectedResourceMetadata?> DiscoverProtectedResourceAsync(
        string serverUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverUrl);

        var prmUrl = BuildWellKnownUrl(serverUrl, PrmWellKnownPath);
        _logger?.LogInformation("发现 Protected Resource Metadata: {Url}", prmUrl);

        var prm = await FetchProtectedResourceMetadataAsync(prmUrl, cancellationToken).ConfigureAwait(false);
        if (prm is null) return null;

        // RFC 9728 §3.3: resource-mismatch 验证（mix-up 保护）
        if (!IsUrlMatching(prm.Resource, serverUrl))
        {
            _logger?.LogWarning("PRM resource 不匹配: expected={Expected}, got={Got}", serverUrl, prm.Resource);
            return null;
        }

        return prm;
    }

    /// <summary>
    /// RFC 9728 → RFC 8414 链式发现
    /// 1. 获取 PRM → 提取 authorization_servers[0]
    /// 2. 对 AS URL 执行 RFC 8414 发现
    /// </summary>
    private async Task<OAuthAuthorizationServerMetadata?> DiscoverViaRfc9728Async(
        string serverUrl,
        CancellationToken cancellationToken)
    {
        var prm = await DiscoverProtectedResourceAsync(serverUrl, cancellationToken).ConfigureAwait(false);
        if (prm is null || prm.AuthorizationServers.Count == 0)
        {
            _logger?.LogInformation("PRM 未找到或无 authorization_servers");
            return null;
        }

        var asUrl = prm.AuthorizationServers[0];
        _logger?.LogInformation("从 PRM 获取 AS URL: {Url}", asUrl);

        return await DiscoverAuthorizationServerAsync(asUrl, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 发现 Authorization Server Metadata — 对齐 TS discoverAuthorizationServer
    /// RFC 8414: GET /.well-known/oauth-authorization-server
    /// </summary>
    public async Task<OAuthAuthorizationServerMetadata?> DiscoverAuthorizationServerAsync(
        string asUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(asUrl);

        var metadataUrl = BuildWellKnownUrl(asUrl, AsWellKnownPath);
        _logger?.LogInformation("发现 Authorization Server Metadata: {Url}", metadataUrl);

        var metadata = await FetchMetadataFromUrlAsync(metadataUrl, cancellationToken).ConfigureAwait(false);
        if (metadata is null) return null;

        // RFC 8414 §3.3: issuer-mismatch 验证
        if (!IsUrlMatching(metadata.Issuer, asUrl))
        {
            _logger?.LogWarning("AS issuer 不匹配: expected={Expected}, got={Got}", asUrl, metadata.Issuer);
            return null;
        }

        // 拒绝非 HTTPS token endpoint
        if (!string.IsNullOrEmpty(metadata.TokenEndpoint) &&
            !metadata.TokenEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("AS token_endpoint 非 HTTPS: {Url}", metadata.TokenEndpoint);
            return null;
        }

        return metadata;
    }

    /// <summary>
    /// 直接对服务器 URL 执行 RFC 8414 发现 — 回退路径
    /// </summary>
    private async Task<OAuthAuthorizationServerMetadata?> DiscoverViaRfc8414Async(
        string serverUrl,
        CancellationToken cancellationToken)
    {
        // 仅当 URL 有路径组件时才尝试（对齐 TS 的路径感知探测）
        var uri = new Uri(serverUrl);
        if (uri.AbsolutePath == "/") return null;

        return await DiscoverAuthorizationServerAsync(serverUrl, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 URL 获取 AS 元数据
    /// </summary>
    private async Task<OAuthAuthorizationServerMetadata?> FetchMetadataFromUrlAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("获取元数据失败: {Url} -> {Status}", url, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, McpOAuthJsonContext.Default.OAuthAuthorizationServerMetadata);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "获取元数据异常: {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// 获取 PRM 元数据
    /// </summary>
    private async Task<OAuthProtectedResourceMetadata?> FetchProtectedResourceMetadataAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("获取 PRM 失败: {Url} -> {Status}", url, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, McpOAuthJsonContext.Default.OAuthProtectedResourceMetadata);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "获取 PRM 异常: {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// 构建 well-known URL — 对齐 TS 的 URL 拼接逻辑
    /// </summary>
    private static string BuildWellKnownUrl(string baseUrl, string wellKnownPath)
    {
        var uri = new Uri(baseUrl);
        // 对齐 TS: well-known 路径拼接在 origin 上
        return $"{uri.Scheme}://{uri.Authority}{wellKnownPath}";
    }

    /// <summary>
    /// URL 匹配检查 — 对齐 TS normalizeUrl 比较
    /// 忽略尾部斜杠和默认端口
    /// </summary>
    private static bool IsUrlMatching(string? url1, string url2)
    {
        if (string.IsNullOrEmpty(url1)) return false;
        return string.Equals(NormalizeUrl(url1), NormalizeUrl(url2), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUrl(string url)
    {
        var uri = new Uri(url);
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{port}{path}";
    }
}

/// <summary>
/// OAuth 2.0 Protected Resource Metadata — RFC 9728
/// </summary>
public sealed partial class OAuthProtectedResourceMetadata
{
    /// <summary>
    /// 资源标识符 — RFC 9728 §2.1 resource
    /// </summary>
    [JsonPropertyName("resource")]
    public string Resource { get; set; } = string.Empty;

    /// <summary>
    /// 授权服务器 URL 列表 — RFC 9728 §2.1 authorization_servers
    /// </summary>
    [JsonPropertyName("authorization_servers")]
    public List<string> AuthorizationServers { get; set; } = new();

    /// <summary>
    /// 资源支持的 scope — RFC 9728 §2.1 scopes_supported
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    public List<string>? ScopesSupported { get; set; }

    /// <summary>
    /// 资源支持的 Bearer 方法 — RFC 9728 §2.1 bearer_methods_supported
    /// </summary>
    [JsonPropertyName("bearer_methods_supported")]
    public List<string>? BearerMethodsSupported { get; set; }

    /// <summary>
    /// 资源文档 URL — RFC 9728 §2.1 resource_documentation
    /// </summary>
    [JsonPropertyName("resource_documentation")]
    public string? ResourceDocumentation { get; set; }
}

/// <summary>
/// OAuth 2.0 Authorization Server Metadata — RFC 8414
/// </summary>
public sealed partial class OAuthAuthorizationServerMetadata
{
    /// <summary>
    /// 发行者标识 — RFC 8414 §2 issuer
    /// </summary>
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// 授权端点 — RFC 8414 §2 authorization_endpoint
    /// </summary>
    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>
    /// 令牌端点 — RFC 8414 §2 token_endpoint
    /// </summary>
    [JsonPropertyName("token_endpoint")]
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// 注册端点 — RFC 8414 §2 registration_endpoint
    /// </summary>
    [JsonPropertyName("registration_endpoint")]
    public string? RegistrationEndpoint { get; set; }

    /// <summary>
    /// 支持的授权类型 — RFC 8414 §2 grant_types_supported
    /// </summary>
    [JsonPropertyName("grant_types_supported")]
    public List<string>? GrantTypesSupported { get; set; }

    /// <summary>
    /// 支持的令牌端点认证方法 — RFC 8414 §2 token_endpoint_auth_methods_supported
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public List<string>? TokenEndpointAuthMethodsSupported { get; set; }

    /// <summary>
    /// 支持的 scope — RFC 8414 §2 scopes_supported
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    public List<string>? ScopesSupported { get; set; }

    /// <summary>
    /// 响应类型 — RFC 8414 §2 response_types_supported
    /// </summary>
    [JsonPropertyName("response_types_supported")]
    public List<string>? ResponseTypesSupported { get; set; }

    /// <summary>
    /// 代码挑战方法 — RFC 8414 §2 code_challenge_methods_supported
    /// </summary>
    [JsonPropertyName("code_challenge_methods_supported")]
    public List<string>? CodeChallengeMethodsSupported { get; set; }

    /// <summary>
    /// 撤销端点 — RFC 8414 §2 revocation_endpoint
    /// </summary>
    [JsonPropertyName("revocation_endpoint")]
    public string? RevocationEndpoint { get; set; }

    /// <summary>
    /// JWKS URI — RFC 8414 §2 jwks_uri
    /// </summary>
    [JsonPropertyName("jwks_uri")]
    public string? JwksUri { get; set; }
}
