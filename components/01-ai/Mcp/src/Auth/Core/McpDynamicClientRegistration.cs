
namespace McpClient;

public sealed partial class McpDynamicClientRegistration
{
    private readonly HttpClient _httpClient;
    [Inject] private readonly ILogger<McpDynamicClientRegistration>? _logger;

    public McpDynamicClientRegistration(HttpClient? httpClient = null, ILogger<McpDynamicClientRegistration>? logger = null)
    {
        // P1-6: fallback 走 HttpClientProviderFactory（支持 JCC_HTTP_MODE=Mock 切换，对齐主程序 IHttpClientProvider 抽象）
        _httpClient = httpClient ?? HttpClientProviderFactory.Create().GetClient();
        _logger = logger;
    }

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

            var result = JsonSerializer.Deserialize(responseBody, McpOAuthJsonContext.Default.DcrRegistrationResult);
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

public sealed partial class DcrClientMetadata
{
    [JsonPropertyName("client_name")]
    public string ClientName { get; set; } = string.Empty;

    [JsonPropertyName("redirect_uris")]
    public List<string> RedirectUris { get; set; } = new();

    [JsonPropertyName("grant_types")]
    public List<string> GrantTypes { get; set; } = new();

    [JsonPropertyName("response_types")]
    public List<string> ResponseTypes { get; set; } = new();

    [JsonPropertyName("token_endpoint_auth_method")]
    public string TokenEndpointAuthMethod { get; set; } = "none";

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

public sealed partial class DcrRegistrationResult
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("client_id_issued_at")]
    public long? ClientIdIssuedAt { get; set; }

    [JsonPropertyName("client_secret_expires_at")]
    public long? ClientSecretExpiresAt { get; set; }
}