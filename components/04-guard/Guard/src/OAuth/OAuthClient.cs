
namespace Services.OAuth;

/// <summary>
/// OAuth 客户端接口
/// 处理 OAuth 2.0 授权流程
/// </summary>
public interface IOAuthClient
{
    /// <summary>
    /// 生成授权 URL
    /// </summary>
    /// <param name="config">OAuth 配置</param>
    /// <param name="state">状态参数</param>
    /// <param name="pkce">PKCE 参数</param>
    /// <returns>授权 URL</returns>
    string BuildAuthorizationUrl(OAuthConfig config, string state, PkceParameters pkce);

    /// <summary>
    /// 交换授权码获取 Token
    /// </summary>
    /// <param name="config">OAuth 配置</param>
    /// <param name="code">授权码</param>
    /// <param name="pkce">PKCE 参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Token 信息</returns>
    Task<OAuthToken> ExchangeCodeAsync(OAuthConfig config, string code, PkceParameters pkce, CancellationToken cancellationToken = default);

    /// <summary>
    /// 刷新 Token
    /// </summary>
    /// <param name="config">OAuth 配置</param>
    /// <param name="refreshToken">刷新令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新的 Token 信息</returns>
    Task<OAuthToken> RefreshTokenAsync(OAuthConfig config, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销 Token
    /// </summary>
    /// <param name="config">OAuth 配置</param>
    /// <param name="token">要撤销的 Token</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RevokeTokenAsync(OAuthConfig config, string token, CancellationToken cancellationToken = default);
}

/// <summary>
/// OAuth 客户端实现
/// </summary>
[Register]
public sealed partial class OAuthClient : IOAuthClient
{
    private readonly HttpClient _httpClient;
    [Inject] private readonly ILogger<OAuthClient>? _logger;
    [Inject] private readonly IClockService _clock;

    public OAuthClient(HttpClient httpClient, ILogger<OAuthClient>? logger = null, IClockService? clock = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <inheritdoc />
    public string BuildAuthorizationUrl(OAuthConfig config, string state, PkceParameters pkce)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(state);
        ArgumentNullException.ThrowIfNull(pkce);

        var scope = string.Join(" ", config.Scope);

        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = config.RedirectUri,
            ["state"] = state,
            ["scope"] = scope
        };

        // 添加 PKCE 参数
        if (config.UsePkce)
        {
            queryParams["code_challenge"] = pkce.CodeChallenge;
            queryParams["code_challenge_method"] = pkce.CodeChallengeMethod;
        }

        // 添加额外参数
        foreach (var param in config.AdditionalParams)
        {
            queryParams[param.Key] = param.Value;
        }

        var queryString = string.Join("&", queryParams.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        var authUrl = $"{config.AuthorizationEndpoint}?{queryString}";

        _logger?.LogDebug("Built authorization URL for {Provider}", config.Provider);

        return authUrl;
    }

    /// <inheritdoc />
    public async Task<OAuthToken> ExchangeCodeAsync(OAuthConfig config, string code, PkceParameters pkce, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(pkce);

        var requestBody = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = config.ClientId,
            ["code"] = code,
            ["redirect_uri"] = config.RedirectUri
        };

        // 添加客户端密钥（如果存在）
        if (!string.IsNullOrEmpty(config.ClientSecret))
        {
            requestBody["client_secret"] = config.ClientSecret;
        }

        // 添加 PKCE verifier
        if (config.UsePkce)
        {
            requestBody["code_verifier"] = pkce.CodeVerifier;
        }

        return await RequestTokenAsync(config.TokenEndpoint, requestBody, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OAuthToken> RefreshTokenAsync(OAuthConfig config, string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        var requestBody = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = config.ClientId,
            ["refresh_token"] = refreshToken
        };

        if (!string.IsNullOrEmpty(config.ClientSecret))
        {
            requestBody["client_secret"] = config.ClientSecret;
        }

        return await RequestTokenAsync(config.TokenEndpoint, requestBody, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RevokeTokenAsync(OAuthConfig config, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(config.RevocationEndpoint))
        {
            _logger?.LogWarning("Revocation endpoint not configured for {Provider}", config.Provider);
            return;
        }

        var requestBody = new Dictionary<string, string>
        {
            ["token"] = token,
            ["client_id"] = config.ClientId
        };

        if (!string.IsNullOrEmpty(config.ClientSecret))
        {
            requestBody["client_secret"] = config.ClientSecret;
        }

        var content = new FormUrlEncodedContent(requestBody);

        try
        {
            var response = await _httpClient.PostAsync(config.RevocationEndpoint, content, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger?.LogInformation("Token revoked successfully for {Provider}", config.Provider);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogWarning("Failed to revoke token for {Provider}: {Error}", config.Provider, error);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error revoking token for {Provider}", config.Provider);
        }
    }

    /// <summary>
    /// 请求 Token
    /// </summary>
    private async Task<OAuthToken> RequestTokenAsync(string tokenEndpoint, Dictionary<string, string> requestBody, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(requestBody);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        _logger?.LogDebug("Requesting token from {Endpoint}", tokenEndpoint);

        var response = await _httpClient.PostAsync(tokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("Token request failed: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new OAuthException($"Token request failed: {response.StatusCode}", response.StatusCode, responseBody);
        }

        var tokenResponse = JsonSerializer.Deserialize(responseBody, TokenResponseJsonContext.Default.TokenResponse);

        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            throw new OAuthException("Invalid token response");
        }

        var expiresAt = _clock.GetUtcNowOffset().AddSeconds(tokenResponse.ExpiresIn);

        var token = new OAuthToken
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            TokenType = tokenResponse.TokenType ?? "Bearer",
            ExpiresAt = expiresAt,
            Scope = tokenResponse.Scope?.Split(' ').ToList() ?? new List<string>()
        };

        _logger?.LogInformation("Token obtained successfully, expires at {ExpiresAt}", expiresAt);

        return token;
    }
}

/// <summary>
/// Token 响应模型
/// </summary>
internal sealed class TokenResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

/// <summary>
/// OAuth 异常
/// </summary>
public partial class OAuthException : Exception
{
    public System.Net.HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }

    public OAuthException(string message) : base(message)
    {
    }

    public OAuthException(string message, System.Net.HttpStatusCode statusCode, string? responseBody) : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
