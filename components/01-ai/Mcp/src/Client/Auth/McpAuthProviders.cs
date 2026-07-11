
namespace McpClient;

/// <summary>
/// API 密钥认证提供者
/// </summary>
public sealed class ApiKeyAuthProvider : IMcpAuthProvider
{
    private readonly string _apiKey;
    private readonly string _headerName;

    public McpAuthType AuthType => McpAuthType.ApiKey;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_apiKey);
    public string? StepUpPendingScope => null;
    public bool NeedsStepUp => false;

    /// <summary>
    /// API Key 值（用于序列化到 McpAuthConfig）
    /// </summary>
    public string ApiKey => _apiKey;

    /// <summary>
    /// 请求头名称
    /// </summary>
    public string HeaderName => _headerName;

    public ApiKeyAuthProvider(string apiKey, string headerName = "X-API-Key")
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _headerName = headerName ?? throw new ArgumentNullException(nameof(headerName));
    }

    public Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, string>
        {
            [_headerName] = _apiKey
        });
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(_apiKey);
    }

    public Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public void MarkStepUpPending(string scope) { /* API Key 不支持 Step-Up */ }
    public void ClearStepUpPending() { /* API Key 不支持 Step-Up */ }
}

/// <summary>
/// Bearer Token 认证提供者
/// </summary>
public sealed class BearerAuthProvider : IMcpAuthProvider
{
    private readonly string _token;

    public McpAuthType AuthType => McpAuthType.Bearer;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_token);
    public string? StepUpPendingScope => null;
    public bool NeedsStepUp => false;

    /// <summary>
    /// Bearer Token 值（用于序列化到 McpAuthConfig）
    /// </summary>
    public string Token => _token;

    public BearerAuthProvider(string token)
    {
        _token = token ?? throw new ArgumentNullException(nameof(token));
    }

    public Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_token}"
        });
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(_token);
    }

    public Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public void MarkStepUpPending(string scope) { /* Bearer 不支持 Step-Up */ }
    public void ClearStepUpPending() { /* Bearer 不支持 Step-Up */ }
}

/// <summary>
/// Basic 认证提供者
/// </summary>
public sealed class BasicAuthProvider : IMcpAuthProvider
{
    private readonly string _username;
    private readonly string _password;

    public McpAuthType AuthType => McpAuthType.Basic;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password);
    public string? StepUpPendingScope => null;
    public bool NeedsStepUp => false;

    /// <summary>
    /// 用户名（用于序列化到 McpAuthConfig）
    /// </summary>
    public string Username => _username;

    /// <summary>
    /// 密码（用于序列化到 McpAuthConfig）
    /// </summary>
    public string Password => _password;

    public BasicAuthProvider(string username, string password)
    {
        _username = username ?? throw new ArgumentNullException(nameof(username));
        _password = password ?? throw new ArgumentNullException(nameof(password));
    }

    public Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
        return Task.FromResult(new Dictionary<string, string>
        {
            ["Authorization"] = $"Basic {credentials}"
        });
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
        return Task.FromResult<string?>(credentials);
    }

    public Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public void MarkStepUpPending(string scope) { /* Basic 不支持 Step-Up */ }
    public void ClearStepUpPending() { /* Basic 不支持 Step-Up */ }
}

/// <summary>
/// OAuth2 认证配置选项
/// </summary>
public sealed record OAuth2ProviderOptions
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string TokenUrl { get; init; }
    public IEnumerable<string>? Scopes { get; init; }
    public HttpClient? HttpClient { get; init; }
    public ILogger? Logger { get; init; }
}

/// <summary>
/// OAuth2 认证提供者
/// </summary>
public sealed class OAuth2AuthProvider : IMcpAuthProvider, IAsyncDisposable
{
    private readonly OAuth2ProviderOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger? _logger;
    private readonly IClockService _clock;
    private readonly List<string> _scopes;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private McpAuthContext _authContext = new();
    private string? _pendingStepUpScope;

    public McpAuthType AuthType => McpAuthType.OAuth2;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_authContext.AccessToken) && !_authContext.IsExpired;
    public string? StepUpPendingScope => _pendingStepUpScope;

    public bool NeedsStepUp
    {
        get
        {
            if (string.IsNullOrEmpty(_pendingStepUpScope)) return false;
            var currentScopes = _authContext.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                ?? Array.Empty<string>();
            return _pendingStepUpScope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(s => !currentScopes.Contains(s, StringComparer.Ordinal));
        }
    }

    public OAuth2AuthProvider(OAuth2ProviderOptions options, IClockService? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        // P1-6: fallback 走 HttpClientProviderFactory（支持 JCC_HTTP_MODE=Mock 切换，对齐主程序 IHttpClientProvider 抽象）
        _httpClient = options.HttpClient ?? HttpClientProviderFactory.Create().GetClient();
        _logger = options.Logger;
        _clock = clock ?? SystemClockService.Instance;
        _scopes = options.Scopes?.ToList() ?? new List<string>();
    }

    public async Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
        // 对齐 TS ClaudeAuthProvider.tokens(): Step-Up 时省略 refresh_token 触发重新授权
        if (NeedsStepUp)
        {
            _logger?.LogWarning("Step-Up 认证待处理，需要提升 scope: {Scope}", _pendingStepUpScope);
            return new Dictionary<string, string>();
        }

        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

        return new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_authContext.AccessToken}"
        };
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        return _authContext.AccessToken;
    }

    public void MarkStepUpPending(string scope)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        _pendingStepUpScope = scope;
        _logger?.LogInformation("Step-Up 认证待处理，所需 scope: {Scope}", scope);
    }

    public void ClearStepUpPending()
    {
        _pendingStepUpScope = null;
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger?.LogInformation("正在刷新 OAuth2 令牌...");

            var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = string.Join(" ", _scopes)
            });

            var response = await _httpClient.PostAsync(_options.TokenUrl, requestContent, cancellationToken).ConfigureAwait(false);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogError("刷新令牌失败: {StatusCode} - {Content}",
                    response.StatusCode, responseContent);
                return false;
            }

            var tokenResponse = JsonSerializer.Deserialize(responseContent, McpClientJsonContext.Default.OAuth2TokenResponse);
            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger?.LogError("无法解析令牌响应");
                return false;
            }

            _authContext.AccessToken = tokenResponse.AccessToken;
            _authContext.RefreshToken = tokenResponse.RefreshToken;
            _authContext.Scope = tokenResponse.Scope ?? _authContext.Scope;

            if (tokenResponse.ExpiresIn > 0)
            {
                _authContext.ExpiresAt = _clock.GetUtcNow().AddSeconds(tokenResponse.ExpiresIn);
            }

            if (!NeedsStepUp)
            {
                ClearStepUpPending();
            }

            _logger?.LogInformation("OAuth2 令牌刷新成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "刷新令牌时发生异常");
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (IsAuthenticated)
        {
            return;
        }

        var success = await RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (!success)
        {
            throw new InvalidOperationException(McpErrorMessages.CannotGetValidAccessToken);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();

        try
        {
            _refreshLock.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"OAuth2AuthProvider: Failed to dispose refresh lock: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        _refreshLock.Dispose();
    }
}

/// <summary>
/// OAuth2 令牌响应
/// </summary>
internal sealed class OAuth2TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

/// <summary>
/// 认证提供者工厂
/// </summary>
public static class McpAuthProviderFactory
{
    public static IMcpAuthProvider Create(McpAuthConfig config, ILogger? logger = null, IHttpClientProvider? httpClientProvider = null)
    {
        return config.Type switch
        {
            McpAuthType.ApiKey => new ApiKeyAuthProvider(
                config.ApiKey ?? throw new ArgumentException(McpErrorMessages.ApiKeyRequired)),

            McpAuthType.Bearer => new BearerAuthProvider(
                config.BearerToken ?? throw new ArgumentException(McpErrorMessages.BearerTokenRequired)),

            McpAuthType.Basic => new BasicAuthProvider(
                config.Username ?? throw new ArgumentException(McpErrorMessages.UsernameRequired),
                config.Password ?? throw new ArgumentException(McpErrorMessages.PasswordRequired)),

            McpAuthType.OAuth2 => new OAuth2AuthProvider(new OAuth2ProviderOptions
            {
                ClientId = config.ClientId ?? throw new ArgumentException(McpErrorMessages.ClientIdRequired),
                ClientSecret = config.ClientSecret ?? throw new ArgumentException(McpErrorMessages.ClientSecretRequired),
                TokenUrl = config.TokenUrl ?? throw new ArgumentException(McpErrorMessages.TokenUrlRequired),
                Scopes = config.Scopes,
                HttpClient = httpClientProvider?.GetClient(),
                Logger = logger,
            }),

            _ => throw new NotSupportedException($"Unsupported auth type: {config.Type}")
        };
    }
}
