
namespace McpClient;

public sealed class ApiKeyAuthProvider : StaticAuthProviderBase
{
    private readonly string _apiKey;
    private readonly string _headerName;

    public override McpAuthType AuthType => McpAuthType.ApiKey;
    public override bool IsAuthenticated => !string.IsNullOrEmpty(_apiKey);

    public string ApiKey => _apiKey;
    public string HeaderName => _headerName;

    public ApiKeyAuthProvider(string apiKey, string headerName = "X-API-Key")
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _headerName = headerName ?? throw new ArgumentNullException(nameof(headerName));
    }

    public override Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, string>
        {
            [_headerName] = _apiKey
        });
    }

    public override Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(_apiKey);
    }
}

public sealed class BearerAuthProvider : StaticAuthProviderBase
{
    private readonly string _token;

    public override McpAuthType AuthType => McpAuthType.Bearer;
    public override bool IsAuthenticated => !string.IsNullOrEmpty(_token);

    public string Token => _token;

    public BearerAuthProvider(string token)
    {
        _token = token ?? throw new ArgumentNullException(nameof(token));
    }

    public override Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_token}"
        });
    }

    public override Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(_token);
    }
}

public sealed class BasicAuthProvider : StaticAuthProviderBase
{
    private readonly string _username;
    private readonly string _password;

    public override McpAuthType AuthType => McpAuthType.Basic;
    public override bool IsAuthenticated => !string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password);

    public string Username => _username;
    public string Password => _password;

    public BasicAuthProvider(string username, string password)
    {
        _username = username ?? throw new ArgumentNullException(nameof(username));
        _password = password ?? throw new ArgumentNullException(nameof(password));
    }

    public override Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
        return Task.FromResult(new Dictionary<string, string>
        {
            ["Authorization"] = $"Basic {credentials}"
        });
    }

    public override Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
        return Task.FromResult<string?>(credentials);
    }
}

public sealed record OAuth2ProviderOptions
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string TokenUrl { get; init; }
    public IEnumerable<string>? Scopes { get; init; }
    public HttpClient? HttpClient { get; init; }
    public ILogger? Logger { get; init; }
}

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
        _httpClient = options.HttpClient ?? HttpClientProviderFactory.Create().GetClient();
        _logger = options.Logger;
        _clock = clock ?? SystemClockService.Instance;
        _scopes = options.Scopes?.ToList() ?? new List<string>();
    }

    public async Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
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
