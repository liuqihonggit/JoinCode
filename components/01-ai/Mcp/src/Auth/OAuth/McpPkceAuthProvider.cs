
namespace McpClient;

public sealed partial class McpPkceAuthProvider : IMcpAuthProvider, IAsyncDisposable
{
    private readonly McpOAuthOptions _options;
    private readonly HttpClient _httpClient;
    [Inject] private readonly ILogger<McpPkceAuthProvider>? _logger;
    private readonly IFileSystem _fs;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly McpOAuthMetadataDiscovery _metadataDiscovery;
    private readonly McpDynamicClientRegistration _dcr;

    private McpAuthContext _authContext = new();
    private string? _codeVerifier;
    private string? _codeChallenge;
    private string? _pendingStepUpScope;
    private string? _resolvedClientId;
    private string? _resolvedAuthorizationUrl;
    private string? _resolvedTokenUrl; // 对齐 TS ClaudeAuthProvider._pendingStepUpScope

    public McpAuthType AuthType => McpAuthType.OAuth2;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_authContext.AccessToken) && !_authContext.IsExpired;

    /// <summary>
    /// 当前 Step-Up 待处理的 scope — 对齐 TS ClaudeAuthProvider._pendingStepUpScope
    /// </summary>
    public string? StepUpPendingScope => _pendingStepUpScope;

    /// <summary>
    /// 是否需要 Step-Up 认证 — 对齐 TS ClaudeAuthProvider.tokens() 中的 needsStepUp 逻辑
    /// 当前 scope 不包含待提升的 scope 时返回 true
    /// </summary>
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

    /// <summary>
    /// 标记 Step-Up 认证待处理 — 对齐 TS ClaudeAuthProvider.markStepUpPending
    /// </summary>
    public void MarkStepUpPending(string scope)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        _pendingStepUpScope = scope;
        _logger?.LogInformation("Step-Up 认证待处理，所需 scope: {Scope}", scope);
    }

    /// <summary>
    /// 清除 Step-Up 状态 — 对齐 TS ClaudeAuthProvider.saveTokens() 中的清除逻辑
    /// </summary>
    public void ClearStepUpPending()
    {
        _pendingStepUpScope = null;
    }

    public McpPkceAuthProvider(
        McpOAuthOptions options,
        IFileSystem fs,
        HttpClient? httpClient = null,
        ILogger<McpPkceAuthProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fs);
        _options = options;
        // P1-6: fallback 走 HttpClientProviderFactory（支持 JCC_HTTP_MODE=Mock 切换，对齐主程序 IHttpClientProvider 抽象）
        _httpClient = httpClient ?? HttpClientProviderFactory.Create().GetClient();
        _logger = logger;
        _fs = fs;
        _metadataDiscovery = new McpOAuthMetadataDiscovery(_httpClient, logger as ILogger<McpOAuthMetadataDiscovery>);
        _dcr = new McpDynamicClientRegistration(_httpClient, logger as ILogger<McpDynamicClientRegistration>);

        if (_options.HasPreconfiguredClient)
        {
            _resolvedClientId = _options.ClientId;
            _resolvedAuthorizationUrl = _options.AuthorizationUrl;
            _resolvedTokenUrl = _options.TokenUrl;
        }
    }

    public async Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
        // 对齐 TS ClaudeAuthProvider.tokens(): Step-Up 时省略 refresh_token 触发重新授权
        if (NeedsStepUp)
        {
            _logger?.LogWarning("Step-Up 认证待处理，需要提升 scope: {Scope}，当前 scope: {CurrentScope}",
                _pendingStepUpScope, _authContext.Scope);
            // 返回空认证头，触发 401 → 重新走 PKCE 授权流程
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

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(_authContext.RefreshToken))
            {
                return await RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            }

            return await AuthorizeWithPkceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "PKCE 认证刷新失败");
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<string> GetAuthorizationUrlAsync(CancellationToken cancellationToken = default)
    {
        await EnsureClientConfiguredAsync(cancellationToken).ConfigureAwait(false);

        GeneratePkceChallenge();

        var queryParams = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _resolvedClientId ?? _options.ClientId ?? throw new InvalidOperationException("ClientId is not set."),
            ["redirect_uri"] = _options.RedirectUrl,
            ["code_challenge"] = _codeChallenge ?? throw new InvalidOperationException("CodeChallenge is not set. Call GeneratePkceChallenge first."),
            ["code_challenge_method"] = "S256"
        };

        if (_options.Scopes.Count > 0)
        {
            queryParams["scope"] = string.Join(" ", _options.Scopes);
        }

        var queryString = string.Join("&", queryParams.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var url = $"{_resolvedAuthorizationUrl}?{queryString}";
        _logger?.LogInformation("PKCE 授权 URL 已生成");

        return url;
    }

    public async Task<bool> ExchangeCodeAsync(string authorizationCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(authorizationCode);

        await EnsureClientConfiguredAsync(cancellationToken).ConfigureAwait(false);

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = authorizationCode,
                ["redirect_uri"] = _options.RedirectUrl,
                ["client_id"] = _resolvedClientId ?? _options.ClientId ?? throw new InvalidOperationException("ClientId is not set."),
                ["code_verifier"] = _codeVerifier ?? string.Empty
            };

            if (!string.IsNullOrEmpty(_options.ClientSecret))
            {
                parameters["client_secret"] = _options.ClientSecret;
            }

            var content = new FormUrlEncodedContent(parameters);
            var response = await _httpClient.PostAsync(_resolvedTokenUrl, content, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogError("授权码交换失败: {StatusCode} - {Body}", response.StatusCode, responseBody);
                return false;
            }

            var tokenResponse = JsonSerializer.Deserialize(responseBody, McpOAuthJsonContext.Default.OAuth2TokenResponse);
            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger?.LogError("无法解析令牌响应");
                return false;
            }

            UpdateAuthContext(tokenResponse);
            await PersistTokenAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("PKCE 授权码交换成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "授权码交换异常");
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

    private async Task EnsureClientConfiguredAsync(CancellationToken cancellationToken)
    {
        if (_resolvedClientId != null && _resolvedAuthorizationUrl != null && _resolvedTokenUrl != null)
        {
            return;
        }

        if (_options.HasPreconfiguredClient)
        {
            _resolvedClientId = _options.ClientId;
            _resolvedAuthorizationUrl = _options.AuthorizationUrl;
            _resolvedTokenUrl = _options.TokenUrl;
            return;
        }

        _logger?.LogInformation("未预配置 OAuth 客户端信息，执行元数据发现 + 动态客户端注册...");

        var serverUrl = _options.AuthorizationUrl;
        if (string.IsNullOrEmpty(serverUrl))
        {
            throw new InvalidOperationException("无法执行 OAuth 元数据发现：缺少服务器 URL");
        }

        var metadata = await _metadataDiscovery.DiscoverAsync(serverUrl, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (metadata == null)
        {
            throw new InvalidOperationException("OAuth 元数据发现失败");
        }

        _resolvedAuthorizationUrl = metadata.AuthorizationEndpoint;
        _resolvedTokenUrl = metadata.TokenEndpoint;

        if (string.IsNullOrEmpty(_resolvedAuthorizationUrl) || string.IsNullOrEmpty(_resolvedTokenUrl))
        {
            throw new InvalidOperationException("OAuth 元数据缺少 authorization_endpoint 或 token_endpoint");
        }

        if (!string.IsNullOrEmpty(_options.ClientId))
        {
            _resolvedClientId = _options.ClientId;
            return;
        }

        if (string.IsNullOrEmpty(metadata.RegistrationEndpoint))
        {
            throw new InvalidOperationException("OAuth 元数据缺少 registration_endpoint，且未预配置 ClientId");
        }

        var scope = metadata.ScopesSupported != null ? string.Join(" ", metadata.ScopesSupported) : null;
        var clientMetadata = McpDynamicClientRegistration.BuildClientMetadata("MCP-Client", _options.RedirectUrl, scope);

        var dcrResult = await _dcr.RegisterAsync(metadata.RegistrationEndpoint, clientMetadata, cancellationToken).ConfigureAwait(false);
        if (dcrResult == null)
        {
            throw new InvalidOperationException("动态客户端注册失败");
        }

        _resolvedClientId = dcrResult.ClientId;
        _logger?.LogInformation("DCR 注册成功: ClientId={ClientId}", _resolvedClientId);
    }

    private async Task<bool> RefreshTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _authContext.RefreshToken ?? throw new InvalidOperationException("RefreshToken is not set."),
                ["client_id"] = _resolvedClientId ?? _options.ClientId
            };

            if (!string.IsNullOrEmpty(_options.ClientSecret))
            {
                parameters["client_secret"] = _options.ClientSecret;
            }

            var content = new FormUrlEncodedContent(parameters);
            var response = await _httpClient.PostAsync(_resolvedTokenUrl ?? _options.TokenUrl, content, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("令牌刷新失败: {StatusCode} - {Body}", response.StatusCode, responseBody);
                _authContext.RefreshToken = null;
                return false;
            }

            var tokenResponse = JsonSerializer.Deserialize(responseBody, McpOAuthJsonContext.Default.OAuth2TokenResponse);
            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger?.LogError("无法解析刷新令牌响应");
                return false;
            }

            UpdateAuthContext(tokenResponse);
            await PersistTokenAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("令牌刷新成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "令牌刷新异常");
            return false;
        }
    }

    private async Task<bool> AuthorizeWithPkceAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_options.TokenStoragePath))
        {
            var loaded = await LoadPersistedTokenAsync(cancellationToken).ConfigureAwait(false);
            if (loaded && IsAuthenticated)
            {
                return true;
            }
        }

        _logger?.LogWarning("PKCE 认证需要用户交互完成授权，请调用 GetAuthorizationUrlAsync 获取授权 URL");
        return false;
    }

    private void GeneratePkceChallenge()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        _codeVerifier = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var challengeBytes = SHA256.HashData(Encoding.UTF8.GetBytes(_codeVerifier));
        _codeChallenge = Convert.ToBase64String(challengeBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private void UpdateAuthContext(global::JoinCode.Abstractions.Models.OAuth.OAuth2TokenResponse tokenResponse)
    {
        _authContext = new McpAuthContext
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken ?? _authContext.RefreshToken,
            ExpiresAt = tokenResponse.ExpiresIn > 0
                ? DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
                : null,
            Scope = tokenResponse.Scope ?? _authContext.Scope, // 对齐 TS tokenData.scope
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {tokenResponse.AccessToken}"
            }
        };

        // 对齐 TS ClaudeAuthProvider.saveTokens(): 保存令牌后清除 Step-Up 状态
        if (!NeedsStepUp)
        {
            ClearStepUpPending();
        }
    }

    private async Task PersistTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.TokenStoragePath))
        {
            return;
        }

        try
        {
            var storage = new PkceTokenStorage
            {
                AccessToken = _authContext.AccessToken,
                RefreshToken = _authContext.RefreshToken,
                ExpiresAt = _authContext.ExpiresAt
            };

            var json = JsonSerializer.Serialize(storage, McpOAuthJsonContext.Default.PkceTokenStorage);
            var directory = Path.GetDirectoryName(_options.TokenStoragePath);
            DirectoryHelper.EnsureDirectoryExists(_fs, directory);

            await _fs.WriteAllTextAsync(_options.TokenStoragePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "令牌持久化失败");
        }
    }

    private async Task<bool> LoadPersistedTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_fs.FileExists(_options.TokenStoragePath))
            {
                return false;
            }

            var json = await _fs.ReadAllTextAsync(_options.TokenStoragePath, cancellationToken).ConfigureAwait(false);
            var storage = JsonSerializer.Deserialize(json, McpOAuthJsonContext.Default.PkceTokenStorage);
            if (storage == null)
            {
                return false;
            }

            _authContext = new McpAuthContext
            {
                AccessToken = storage.AccessToken,
                RefreshToken = storage.RefreshToken,
                ExpiresAt = storage.ExpiresAt
            };

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载持久化令牌失败");
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _refreshLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        _refreshLock.Dispose();
    }
}

public sealed partial class PkceTokenStorage
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }
}
