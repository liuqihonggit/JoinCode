
namespace McpClient;

/// <summary>
/// API Key 认证提供者 — 将 API Key 通过指定 HTTP 头注入请求。
/// </summary>
public sealed class ApiKeyAuthProvider : StaticAuthProviderBase
{
    private readonly string _apiKey;
    private readonly string _headerName;

    /// <summary>认证类型 — 固定为 ApiKey。</summary>
    public override McpAuthType AuthType => McpAuthType.ApiKey;

    /// <summary>认证是否有效 — API Key 非空即有效。</summary>
    public override bool IsAuthenticated => !string.IsNullOrEmpty(_apiKey);

    /// <summary>API Key 值。</summary>
    public string ApiKey => _apiKey;

    /// <summary>携带 API Key 的请求头名称。</summary>
    public string HeaderName => _headerName;

    /// <summary>
    /// 构造 ApiKeyAuthProvider 实例。
    /// </summary>
    /// <param name="apiKey">API Key 字符串。</param>
    /// <param name="headerName">携带 API Key 的头名称,默认为 X-API-Key。</param>
    public ApiKeyAuthProvider(string apiKey, string headerName = "X-API-Key")
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _headerName = headerName ?? throw new ArgumentNullException(nameof(headerName));
    }

    /// <summary>
    /// 获取认证头 — 返回包含 API Key 的单元素字典。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含头名与 API Key 的字典。</returns>
    public override Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, string>
        {
            [_headerName] = _apiKey
        });
    }

    /// <summary>
    /// 获取访问令牌 — 返回 API Key 作为令牌。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>API Key 字符串。</returns>
    public override Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(_apiKey);
    }
}

/// <summary>
/// Bearer Token 认证提供者 — 将令牌以 "Bearer {token}" 形式注入 Authorization 头。
/// </summary>
public sealed class BearerAuthProvider : StaticAuthProviderBase
{
    private readonly string _token;

    /// <summary>认证类型 — 固定为 Bearer。</summary>
    public override McpAuthType AuthType => McpAuthType.Bearer;

    /// <summary>认证是否有效 — 令牌非空即有效。</summary>
    public override bool IsAuthenticated => !string.IsNullOrEmpty(_token);

    /// <summary>Bearer 令牌值。</summary>
    public string Token => _token;

    /// <summary>
    /// 构造 BearerAuthProvider 实例。
    /// </summary>
    /// <param name="token">Bearer 令牌字符串。</param>
    public BearerAuthProvider(string token)
    {
        _token = token ?? throw new ArgumentNullException(nameof(token));
    }

    /// <summary>
    /// 获取认证头 — 返回包含 "Bearer {token}" 的 Authorization 头。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含 Authorization 头的字典。</returns>
    public override Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_token}"
        });
    }

    /// <summary>
    /// 获取访问令牌 — 返回 Bearer 令牌。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Bearer 令牌字符串。</returns>
    public override Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(_token);
    }
}

/// <summary>
/// Basic 认证提供者 — 将 "username:password" Base64 编码后以 "Basic {credentials}" 形式注入 Authorization 头。
/// </summary>
public sealed class BasicAuthProvider : StaticAuthProviderBase
{
    private readonly string _username;
    private readonly string _password;

    /// <summary>认证类型 — 固定为 Basic。</summary>
    public override McpAuthType AuthType => McpAuthType.Basic;

    /// <summary>认证是否有效 — 用户名与密码均非空即有效。</summary>
    public override bool IsAuthenticated => !string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password);

    /// <summary>用户名。</summary>
    public string Username => _username;

    /// <summary>密码。</summary>
    public string Password => _password;

    /// <summary>
    /// 构造 BasicAuthProvider 实例。
    /// </summary>
    /// <param name="username">用户名。</param>
    /// <param name="password">密码。</param>
    public BasicAuthProvider(string username, string password)
    {
        _username = username ?? throw new ArgumentNullException(nameof(username));
        _password = password ?? throw new ArgumentNullException(nameof(password));
    }

    /// <summary>
    /// 获取认证头 — 返回包含 Base64 编码凭证的 Authorization 头。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含 Authorization 头的字典。</returns>
    public override Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
        return Task.FromResult(new Dictionary<string, string>
        {
            ["Authorization"] = $"Basic {credentials}"
        });
    }

    /// <summary>
    /// 获取访问令牌 — 返回 Base64 编码后的凭证字符串。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Base64 编码凭证字符串。</returns>
    public override Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
        return Task.FromResult<string?>(credentials);
    }
}

/// <summary>
/// OAuth2 认证提供者选项 — 描述客户端凭证、令牌端点与 scope 等 OAuth2 配置。
/// </summary>
public sealed record OAuth2ProviderOptions
{
    /// <summary>OAuth2 客户端 ID。</summary>
    public required string ClientId { get; init; }

    /// <summary>OAuth2 客户端密钥。</summary>
    public required string ClientSecret { get; init; }

    /// <summary>令牌端点 URL。</summary>
    public required string TokenUrl { get; init; }

    /// <summary>OAuth2 scope 列表,可为 null。</summary>
    public IEnumerable<string>? Scopes { get; init; }

    /// <summary>自定义 HttpClient,为 null 时使用默认工厂创建。</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>日志记录器。</summary>
    public ILogger? Logger { get; init; }
}

/// <summary>
/// OAuth2 认证提供者 — 基于 client_credentials 授权类型获取与刷新访问令牌,支持 Step-Up 认证与并发安全的令牌刷新。
/// </summary>
public sealed class OAuth2AuthProvider : IMcpAuthProvider, IAsyncDisposable
{
    private readonly OAuth2ProviderOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger? _logger;
    private readonly IClockService _clock;
    private readonly List<string> _scopes;
    private readonly AsyncLock _refreshLock = new();

    private McpAuthContext _authContext = new();
    private string? _pendingStepUpScope;
    private int _disposed;

    /// <summary>认证类型 — 固定为 OAuth2。</summary>
    public McpAuthType AuthType => McpAuthType.OAuth2;

    /// <summary>认证是否有效 — 访问令牌非空且未过期。</summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_authContext.AccessToken) && !_authContext.IsExpired;

    /// <summary>当前 Step-Up 待处理的 scope 字符串。</summary>
    public string? StepUpPendingScope => _pendingStepUpScope;

    /// <summary>是否需要 Step-Up 认证 — 当前 scope 不包含待提升 scope 时为 true。</summary>
    public bool NeedsStepUp
    {
        get
        {
            if (string.IsNullOrEmpty(_pendingStepUpScope)) return false;
            var currentScopes = new HashSet<string>(
                _authContext.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
                StringComparer.Ordinal);
            return _pendingStepUpScope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(s => !currentScopes.Contains(s));
        }
    }

    /// <summary>
    /// 构造 OAuth2AuthProvider 实例。
    /// </summary>
    /// <param name="options">OAuth2 提供者选项。</param>
    /// <param name="clock">时钟服务,为 null 时使用系统时钟,用于测试注入。</param>
    public OAuth2AuthProvider(OAuth2ProviderOptions options, IClockService? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _httpClient = options.HttpClient ?? HttpClientProviderFactory.Create().GetClient();
        _logger = options.Logger;
        _clock = clock ?? SystemClockService.Instance;
        _scopes = options.Scopes?.ToList() ?? new List<string>();
    }

    /// <summary>
    /// 获取认证头 — 若待 Step-Up 返回空字典,否则确保令牌有效后返回 Authorization Bearer 头。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含 Authorization 头的字典,Step-Up 待处理时为空字典。</returns>
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

    /// <summary>
    /// 获取访问令牌 — 确保令牌有效后返回当前访问令牌。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>访问令牌字符串。</returns>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        return _authContext.AccessToken;
    }

    /// <summary>
    /// 标记 Step-Up 认证待处理 — 当服务端返回 403 + insufficient_scope 时调用。
    /// </summary>
    /// <param name="scope">待提升的 scope 字符串。</param>
    public void MarkStepUpPending(string scope)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        _pendingStepUpScope = scope;
        _logger?.LogInformation("Step-Up 认证待处理，所需 scope: {Scope}", scope);
    }

    /// <summary>清除 Step-Up 待处理状态 — 令牌保存后调用。</summary>
    public void ClearStepUpPending()
    {
        _pendingStepUpScope = null;
    }

    /// <summary>
    /// 刷新 OAuth2 访问令牌 — 使用 client_credentials 授权类型向令牌端点请求新令牌,并发安全。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>刷新成功返回 true,失败返回 false。</returns>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var guard = await _refreshLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_refreshLock.Name}' 等待超时");
        try
        {
            _logger?.LogInformation("正在刷新 OAuth2 令牌...");

            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = string.Join(" ", _scopes)
            };

            var tokenResponse = await OAuth2TokenExchange.ExchangeTokenAsync(
                _httpClient, _options.TokenUrl, parameters,
                McpClientJsonContext.Default.OAuth2TokenResponse, _logger, cancellationToken).ConfigureAwait(false);

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
        catch (OAuthException ex)
        {
            _logger?.LogError(ex, "刷新令牌失败");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "刷新令牌时发生异常");
            return false;
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

    /// <summary>
    /// 同步释放资源 — 释放 HttpClient 与刷新锁。
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();

        try
        {
            _refreshLock.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "OAuth2AuthProvider: 释放 refresh lock 失败");
        }
    }

    /// <summary>
    /// 异步释放资源 — 释放 HttpClient 与刷新锁,幂等保护防止重复释放。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _httpClient.Dispose();
        _refreshLock.Dispose();
    }
}

/// <summary>
/// MCP 认证提供者工厂 — 根据 McpAuthConfig 类型创建对应的认证提供者实例。
/// </summary>
public static class McpAuthProviderFactory
{
    /// <summary>
    /// 根据认证配置创建对应的认证提供者实例 — 支持 ApiKey/Bearer/Basic/OAuth2 四种类型。
    /// </summary>
    /// <param name="config">认证配置。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="httpClientProvider">HTTP 客户端提供者,用于 OAuth2 场景。</param>
    /// <returns>对应类型的 IMcpAuthProvider 实例。</returns>
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
