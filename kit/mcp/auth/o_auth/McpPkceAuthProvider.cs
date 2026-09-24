
namespace McpClient;

/// <summary>
/// MCP PKCE 认证提供者 — 对齐 TS ClaudeAuthProvider
/// 实现 OAuth 2.0 PKCE 授权流程：元数据发现 → DCR → 授权码交换 → 令牌刷新
/// </summary>
public sealed partial class McpPkceAuthProvider : IMcpAuthProvider, IAsyncDisposable {
    private readonly McpOAuthOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<McpPkceAuthProvider>? _logger;
    private readonly IFileSystem _fs;
    private readonly AsyncLock _refreshLock = new();
    private readonly McpOAuthMetadataDiscovery _metadataDiscovery;
    private readonly McpDynamicClientRegistration _dcr;

    private McpAuthContext _authContext = new();
    private string? _codeVerifier;
    private string? _codeChallenge;
    private string? _pendingStepUpScope;
    private string? _resolvedClientId;
    private string? _resolvedAuthorizationUrl;
    private string? _resolvedTokenUrl; // 对齐 TS ClaudeAuthProvider._pendingStepUpScope
    private int _disposed;
    private volatile bool _needsStepUpCache;
    private volatile bool _needsStepUpCacheValid;

    /// <summary>
    /// 认证类型 — 固定为 OAuth2
    /// </summary>
    public McpAuthType AuthType => McpAuthType.OAuth2;

    /// <summary>
    /// 是否已认证 — 访问令牌非空且未过期
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_authContext.AccessToken) && !_authContext.IsExpired;

    /// <summary>
    /// 当前 Step-Up 待处理的 scope — 对齐 TS ClaudeAuthProvider._pendingStepUpScope
    /// </summary>
    public string? StepUpPendingScope => _pendingStepUpScope;

    /// <summary>
    /// 是否需要 Step-Up 认证 — 对齐 TS ClaudeAuthProvider.tokens() 中的 needsStepUp 逻辑
    /// 当前 scope 不包含待提升的 scope 时返回 true
    /// </summary>
    public bool NeedsStepUp {
        get {
            if (_needsStepUpCacheValid) return _needsStepUpCache;
            _needsStepUpCache = ComputeNeedsStepUp();
            _needsStepUpCacheValid = true;
            return _needsStepUpCache;
        }
    }

    private bool ComputeNeedsStepUp() {
        if (string.IsNullOrEmpty(_pendingStepUpScope)) return false;
        var currentScopes = new HashSet<string>(
            _authContext.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
            StringComparer.Ordinal);
        return _pendingStepUpScope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(s => !currentScopes.Contains(s));
    }

    /// <summary>
    /// 标记 Step-Up 认证待处理 — 对齐 TS ClaudeAuthProvider.markStepUpPending
    /// </summary>
    public void MarkStepUpPending(string scope) {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        _pendingStepUpScope = scope;
        _needsStepUpCacheValid = false;
        _logger?.LogInformation("Step-Up 认证待处理，所需 scope: {Scope}", scope);
    }

    /// <summary>
    /// 清除 Step-Up 状态 — 对齐 TS ClaudeAuthProvider.saveTokens() 中的清除逻辑
    /// </summary>
    public void ClearStepUpPending() {
        _pendingStepUpScope = null;
        _needsStepUpCacheValid = false;
    }

    /// <summary>
    /// 创建 McpPkceAuthProvider 实例
    /// </summary>
    /// <param name="options">OAuth 选项</param>
    /// <param name="fs">文件系统抽象（用于令牌持久化）</param>
    /// <param name="httpClient">HTTP 客户端（为 null 时走 HttpClientProviderFactory fallback）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public McpPkceAuthProvider(
        McpOAuthOptions options,
        IFileSystem fs,
        HttpClient? httpClient = null,
        ILogger<McpPkceAuthProvider>? logger = null) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fs);
        _options = options;
        // P1-6: fallback 走 HttpClientProviderFactory（支持 JCC_HTTP_MODE=Mock 切换，对齐主程序 IHttpClientProvider 抽象）
        _httpClient = httpClient ?? HttpClientProviderFactory.Create().GetClient();
        _logger = logger;
        _fs = fs;
        _metadataDiscovery = new McpOAuthMetadataDiscovery(_httpClient, logger as ILogger<McpOAuthMetadataDiscovery>);
        _dcr = new McpDynamicClientRegistration(_httpClient, logger as ILogger<McpDynamicClientRegistration>);

        if (_options.HasPreconfiguredClient) {
            _resolvedClientId = _options.ClientId;
            _resolvedAuthorizationUrl = _options.AuthorizationUrl;
            _resolvedTokenUrl = _options.TokenUrl;
        }
    }

    /// <summary>
    /// 异步获取认证头 — Step-Up 待处理时返回空字典触发 401 重新授权
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>认证头字典（含 Authorization: Bearer xxx）</returns>
    public async Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default) {
        // 对齐 TS ClaudeAuthProvider.tokens(): Step-Up 时省略 refresh_token 触发重新授权
        if (NeedsStepUp) {
            _logger?.LogWarning("Step-Up 认证待处理，需要提升 scope: {Scope}，当前 scope: {CurrentScope}",
                _pendingStepUpScope, _authContext.Scope);
            // 返回空认证头，触发 401 → 重新走 PKCE 授权流程
            return new Dictionary<string, string>();
        }

        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

        return new Dictionary<string, string> {
            ["Authorization"] = $"Bearer {_authContext.AccessToken}"
        };
    }

    /// <summary>
    /// 异步获取访问令牌 — 必要时自动刷新
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>访问令牌；获取失败抛出 InvalidOperationException</returns>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        return _authContext.AccessToken;
    }

    /// <summary>
    /// 异步刷新认证 — 有 refresh_token 走令牌刷新，否则走 PKCE 重新授权
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>刷新成功返回 true；失败返回 false</returns>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default) {
        using var guard = await _refreshLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_refreshLock.Name}' 等待超时");
        try {
            if (!string.IsNullOrEmpty(_authContext.RefreshToken)) {
                return await RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            }

            return await AuthorizeWithPkceAsync(cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogError(ex, "PKCE 认证刷新失败");
            return false;
        }

    }

    /// <summary>
    /// 异步生成 PKCE 授权 URL — 生成 code_verifier/code_challenge 后拼接授权端点查询参数
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>授权 URL（含 response_type、client_id、redirect_uri、code_challenge 等）</returns>
    public async Task<string> GetAuthorizationUrlAsync(CancellationToken cancellationToken = default) {
        await EnsureClientConfiguredAsync(cancellationToken).ConfigureAwait(false);

        GeneratePkceChallenge();

        var queryParams = new Dictionary<string, string> {
            ["response_type"] = "code",
            ["client_id"] = _resolvedClientId ?? _options.ClientId ?? throw new InvalidOperationException("ClientId is not set."),
            ["redirect_uri"] = _options.RedirectUrl,
            ["code_challenge"] = _codeChallenge ?? throw new InvalidOperationException("CodeChallenge is not set. Call GeneratePkceChallenge first."),
            ["code_challenge_method"] = "S256"
        };

        if (_options.Scopes.Any()) {
            queryParams["scope"] = string.Join(" ", _options.Scopes);
        }

        var queryString = string.Join("&", queryParams.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var url = $"{_resolvedAuthorizationUrl}?{queryString}";
        _logger?.LogInformation("PKCE 授权 URL 已生成");

        return url;
    }

    /// <summary>
    /// 异步交换授权码为访问令牌 — 对齐 OAuth 2.0 authorization_code grant
    /// </summary>
    /// <param name="authorizationCode">从授权回调获取的授权码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>交换成功返回 true；失败返回 false</returns>
    public async Task<bool> ExchangeCodeAsync(string authorizationCode, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(authorizationCode);

        await EnsureClientConfiguredAsync(cancellationToken).ConfigureAwait(false);

        using var guard = await _refreshLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_refreshLock.Name}' 等待超时");
        try {
            var parameters = new Dictionary<string, string> {
                ["grant_type"] = "authorization_code",
                ["code"] = authorizationCode,
                ["redirect_uri"] = _options.RedirectUrl,
                ["client_id"] = _resolvedClientId ?? _options.ClientId ?? throw new InvalidOperationException("ClientId is not set."),
                ["code_verifier"] = _codeVerifier ?? string.Empty
            };

            if (!string.IsNullOrEmpty(_options.ClientSecret)) {
                parameters["client_secret"] = _options.ClientSecret;
            }

            var tokenResponse = await OAuth2TokenExchange.ExchangeTokenAsync(
                _httpClient, _resolvedTokenUrl!, parameters,
                McpOAuthJsonContext.Default.OAuth2TokenResponse, _logger, cancellationToken).ConfigureAwait(false);

            UpdateAuthContext(tokenResponse);
            await PersistTokenAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("PKCE 授权码交换成功");
            return true;
        } catch (OAuthException ex) {
            _logger?.LogError(ex, "授权码交换失败");
            return false;
        } catch (Exception ex) {
            _logger?.LogError(ex, "授权码交换异常");
            return false;
        }

    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken) {
        if (IsAuthenticated) {
            return;
        }

        var success = await RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (!success) {
            throw new InvalidOperationException(McpErrorMessages.CannotGetValidAccessToken);
        }
    }

    private async Task EnsureClientConfiguredAsync(CancellationToken cancellationToken) {
        if (_resolvedClientId != null && _resolvedAuthorizationUrl != null && _resolvedTokenUrl != null) {
            return;
        }

        if (_options.HasPreconfiguredClient) {
            _resolvedClientId = _options.ClientId;
            _resolvedAuthorizationUrl = _options.AuthorizationUrl;
            _resolvedTokenUrl = _options.TokenUrl;
            return;
        }

        _logger?.LogInformation("未预配置 OAuth 客户端信息，执行元数据发现 + 动态客户端注册...");

        var serverUrl = _options.AuthorizationUrl;
        if (string.IsNullOrEmpty(serverUrl)) {
            throw new InvalidOperationException("[MCP010] 无法执行 OAuth 元数据发现：缺少服务器 URL");
        }

        var metadata = await _metadataDiscovery.DiscoverAsync(serverUrl, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (metadata == null) {
            throw new InvalidOperationException("[MCP011] OAuth 元数据发现失败");
        }

        _resolvedAuthorizationUrl = metadata.AuthorizationEndpoint;
        _resolvedTokenUrl = metadata.TokenEndpoint;

        if (string.IsNullOrEmpty(_resolvedAuthorizationUrl) || string.IsNullOrEmpty(_resolvedTokenUrl)) {
            throw new InvalidOperationException("[MCP012] OAuth 元数据缺少 authorization_endpoint 或 token_endpoint");
        }

        if (!string.IsNullOrEmpty(_options.ClientId)) {
            _resolvedClientId = _options.ClientId;
            return;
        }

        if (string.IsNullOrEmpty(metadata.RegistrationEndpoint)) {
            throw new InvalidOperationException("[MCP013] OAuth 元数据缺少 registration_endpoint，且未预配置 ClientId");
        }

        var scope = metadata.ScopesSupported != null ? string.Join(" ", metadata.ScopesSupported) : null;
        var clientMetadata = McpDynamicClientRegistration.BuildClientMetadata("MCP-Client", _options.RedirectUrl, scope);

        var dcrResult = await _dcr.RegisterAsync(metadata.RegistrationEndpoint, clientMetadata, cancellationToken).ConfigureAwait(false);
        if (dcrResult == null) {
            throw new InvalidOperationException("[MCP014] 动态客户端注册失败");
        }

        _resolvedClientId = dcrResult.ClientId;
        _logger?.LogInformation("DCR 注册成功: ClientId={ClientId}", _resolvedClientId);
    }

    private async Task<bool> RefreshTokenAsync(CancellationToken cancellationToken) {
        try {
            var parameters = new Dictionary<string, string> {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _authContext.RefreshToken ?? throw new InvalidOperationException("RefreshToken is not set."),
                ["client_id"] = _resolvedClientId ?? _options.ClientId
            };

            if (!string.IsNullOrEmpty(_options.ClientSecret)) {
                parameters["client_secret"] = _options.ClientSecret;
            }

            var tokenResponse = await OAuth2TokenExchange.ExchangeTokenAsync(
                _httpClient, _resolvedTokenUrl ?? _options.TokenUrl, parameters,
                McpOAuthJsonContext.Default.OAuth2TokenResponse, _logger, cancellationToken).ConfigureAwait(false);

            UpdateAuthContext(tokenResponse);
            await PersistTokenAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("令牌刷新成功");
            return true;
        } catch (OAuthException ex) {
            _logger?.LogWarning(ex, "令牌刷新失败");
            _authContext.RefreshToken = null;
            return false;
        } catch (Exception ex) {
            _logger?.LogError(ex, "令牌刷新异常");
            return false;
        }
    }

    private async Task<bool> AuthorizeWithPkceAsync(CancellationToken cancellationToken) {
        if (!string.IsNullOrEmpty(_options.TokenStoragePath)) {
            var loaded = await LoadPersistedTokenAsync(cancellationToken).ConfigureAwait(false);
            if (loaded && IsAuthenticated) {
                return true;
            }
        }

        _logger?.LogWarning("PKCE 认证需要用户交互完成授权，请调用 GetAuthorizationUrlAsync 获取授权 URL");
        return false;
    }

    private void GeneratePkceChallenge() {
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

    private void UpdateAuthContext(global::JoinCode.Abstractions.Models.OAuth.OAuth2TokenResponse tokenResponse) {
        _needsStepUpCacheValid = false;
        _authContext = new McpAuthContext {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken ?? _authContext.RefreshToken,
            ExpiresAt = tokenResponse.ExpiresIn > 0
                ? DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
                : null,
            Scope = tokenResponse.Scope ?? _authContext.Scope, // 对齐 TS tokenData.scope
            Headers = new Dictionary<string, string> {
                ["Authorization"] = $"Bearer {tokenResponse.AccessToken}"
            }
        };

        // 对齐 TS ClaudeAuthProvider.saveTokens(): 保存令牌后清除 Step-Up 状态
        if (!NeedsStepUp) {
            ClearStepUpPending();
        }
    }

    private async Task PersistTokenAsync(CancellationToken cancellationToken) {
        if (string.IsNullOrEmpty(_options.TokenStoragePath)) {
            return;
        }

        try {
            var storage = new PkceTokenStorage {
                AccessToken = _authContext.AccessToken,
                RefreshToken = _authContext.RefreshToken,
                ExpiresAt = _authContext.ExpiresAt
            };

            var json = JsonSerializer.Serialize(storage, McpOAuthJsonContext.Default.PkceTokenStorage);
            var directory = Path.GetDirectoryName(_options.TokenStoragePath);
            DirectoryHelper.EnsureDirectoryExists(_fs, directory);

            await _fs.WriteAllTextAsync(_options.TokenStoragePath, json, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "令牌持久化失败");
        }
    }

    private async Task<bool> LoadPersistedTokenAsync(CancellationToken cancellationToken) {
        try {
            if (!_fs.FileExists(_options.TokenStoragePath)) {
                return false;
            }

            var storage = await _fs.ReadAndDeserializeAsync(_options.TokenStoragePath, McpOAuthJsonContext.Default.PkceTokenStorage, cancellationToken).ConfigureAwait(false);
            if (storage == null) {
                return false;
            }

        _needsStepUpCacheValid = false;
        _authContext = new McpAuthContext {
            AccessToken = storage.AccessToken,
            RefreshToken = storage.RefreshToken,
            ExpiresAt = storage.ExpiresAt
        };

            return true;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "加载持久化令牌失败");
            return false;
        }
    }

    /// <summary>
    /// 释放同步资源 — HttpClient 和刷新锁
    /// </summary>
    public void Dispose() {
        _httpClient.Dispose();
        _refreshLock.Dispose();
    }

    /// <summary>
    /// 释放异步资源 — 幂等，多次调用安全
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _httpClient.Dispose();
        _refreshLock.Dispose();
    }
}

/// <summary>
/// PKCE 令牌持久化存储 — 序列化到本地文件的令牌三元组
/// </summary>
public sealed partial class PkceTokenStorage {
    /// <summary>
    /// 访问令牌
    /// </summary>
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    /// <summary>
    /// 刷新令牌
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// 过期时间（UTC）
    /// </summary>
    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }
}