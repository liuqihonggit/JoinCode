
namespace McpClient;

/// <summary>
/// OAuth 2.0 动态客户端注册（DCR）服务 — 对齐 RFC 7591
/// 在未预配置客户端信息时，向授权服务器的 registration_endpoint 注册新客户端
/// </summary>
public sealed partial class McpDynamicClientRegistration
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<McpDynamicClientRegistration>? _logger;

    /// <summary>
    /// 创建 McpDynamicClientRegistration 实例
    /// </summary>
    /// <param name="httpClient">HTTP 客户端（为 null 时走 HttpClientProviderFactory fallback）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public McpDynamicClientRegistration(HttpClient? httpClient = null, ILogger<McpDynamicClientRegistration>? logger = null)
    {
        // P1-6: fallback 走 HttpClientProviderFactory（支持 JCC_HTTP_MODE=Mock 切换，对齐主程序 IHttpClientProvider 抽象）
        _httpClient = httpClient ?? HttpClientProviderFactory.Create().GetClient();
        _logger = logger;
    }

    /// <summary>
    /// 向授权服务器执行动态客户端注册
    /// </summary>
    /// <param name="registrationEndpoint">注册端点 URL</param>
    /// <param name="metadata">客户端元数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注册结果（失败返回 null）</returns>
    public async Task<DcrRegistrationResult?> RegisterAsync(
        string registrationEndpoint,
        DcrClientMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationEndpoint);
        ArgumentNullException.ThrowIfNull(metadata);

        _logger?.LogInformation("执行动态客户端注册: {Url}", registrationEndpoint);

        try
        {
            var json = JsonSerializer.Serialize(metadata, McpOAuthJsonContext.Default.DcrClientMetadata);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(registrationEndpoint, content, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogError("动态客户端注册失败: {StatusCode} - {Body}", response.StatusCode, responseBody);
                return null;
            }

            var result = RelaxedJsonSerializer.Deserialize(responseBody, McpOAuthJsonContext.Default.DcrRegistrationResult);
            if (result == null || string.IsNullOrEmpty(result.ClientId))
            {
                _logger?.LogError("无法解析动态客户端注册响应");
                return null;
            }

            _logger?.LogInformation("动态客户端注册成功: ClientId={ClientId}", result.ClientId);
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "动态客户端注册异常");
            return null;
        }
    }

    /// <summary>
    /// 构建客户端元数据 — 对齐 DCR 标准字段
    /// </summary>
    /// <param name="serverName">服务器名称（用于拼接 ClientName）</param>
    /// <param name="redirectUri">回调重定向 URI</param>
    /// <param name="scope">申请的 scope（可选）</param>
    /// <returns>客户端元数据实例</returns>
    public static DcrClientMetadata BuildClientMetadata(string serverName, string redirectUri, string? scope = null)
    {
        var metadata = new DcrClientMetadata
        {
            ClientName = $"JoinCode ({serverName})",
            RedirectUris = [redirectUri],
            GrantTypes = ["authorization_code", "refresh_token"],
            ResponseTypes = ["code"],
            TokenEndpointAuthMethod = "none"
        };

        if (!string.IsNullOrEmpty(scope))
        {
            metadata.Scope = scope;
        }

        return metadata;
    }
}

/// <summary>
/// DCR 客户端元数据 — 对齐 RFC 7591 §2 Registration Request
/// </summary>
public sealed partial class DcrClientMetadata
{
    /// <summary>
    /// 客户端名称
    /// </summary>
    [JsonPropertyName("client_name")]
    public string ClientName { get; set; } = string.Empty;

    /// <summary>
    /// 回调重定向 URI 列表
    /// </summary>
    [JsonPropertyName("redirect_uris")]
    public List<string> RedirectUris { get; set; } = new();

    /// <summary>
    /// 支持的授权类型列表
    /// </summary>
    [JsonPropertyName("grant_types")]
    public List<string> GrantTypes { get; set; } = new();

    /// <summary>
    /// 支持的响应类型列表
    /// </summary>
    [JsonPropertyName("response_types")]
    public List<string> ResponseTypes { get; set; } = new();

    /// <summary>
    /// 令牌端点认证方法（默认 none，PKCE 公开客户端）
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_method")]
    public string TokenEndpointAuthMethod { get; set; } = "none";

    /// <summary>
    /// 申请的 scope（可选）
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

/// <summary>
/// DCR 注册结果 — 对齐 RFC 7591 §2 Registration Response
/// </summary>
public sealed partial class DcrRegistrationResult
{
    /// <summary>
    /// 注册的客户端 ID
    /// </summary>
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// 注册的客户端密钥（可选，PKCE 公开客户端通常无）
    /// </summary>
    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; set; }

    /// <summary>
    /// 客户端 ID 签发时间戳（Unix 秒）
    /// </summary>
    [JsonPropertyName("client_id_issued_at")]
    public long? ClientIdIssuedAt { get; set; }

    /// <summary>
    /// 客户端密钥过期时间戳（Unix 秒，0 表示永不过期）
    /// </summary>
    [JsonPropertyName("client_secret_expires_at")]
    public long? ClientSecretExpiresAt { get; set; }
}