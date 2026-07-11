

namespace McpToolHandlers;

/// <summary>
/// MCP 认证工具处理器 - 提供 MCP 服务器认证功能
/// </summary>
[McpToolHandler(ToolCategory.McpAuth)]
public class McpAuthToolHandlers : IAsyncDisposable, IMcpAuthConfigProvider
{
    private readonly Dictionary<string, IMcpAuthProvider> _authProviders = new();
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private readonly IMcpAuthPersistenceService? _persistenceService;
    private readonly IHttpClientProvider? _httpClientProvider;

    public McpAuthToolHandlers(ILogger? logger = null, IMcpAuthPersistenceService? persistenceService = null, IHttpClientProvider? httpClientProvider = null)
    {
        _logger = logger;
        _persistenceService = persistenceService;
        _httpClientProvider = httpClientProvider;
    }

    /// <summary>
    /// 配置 API 密钥认证
    /// </summary>
    [McpTool(McpToolNameConstants.McpAuthApiKey, "Configure API key authentication for MCP server", "mcp")]
    public async Task<ToolResult> McpAuthApiKeyAsync(
        [McpToolParameter("Authentication config name")] string auth_name,
        [McpToolParameter("API key")] string api_key,
        [McpToolParameter("Header name", Required = false, DefaultValue = "X-API-Key")] string header_name = "X-API-Key",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(auth_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.AuthNameCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(api_key))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ApiKeyCannotBeEmpty)).Build();
        }

        try
        {
            var provider = new ApiKeyAuthProvider(api_key, header_name);
            _authProviders[auth_name] = provider;

            await PersistAuthConfigAsync(auth_name, McpAuthConfigType.ApiKey, cancellationToken).ConfigureAwait(false);

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.ApiKeyAuthConfigured, auth_name));
            response.AppendLine(L.T(StringKey.LabelHeader, header_name));

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ConfigureApiKeyAuthFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConfigurationFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 配置 Bearer Token 认证
    /// </summary>
    [McpTool(McpToolNameConstants.McpAuthBearer, "Configure Bearer Token authentication for MCP server", "mcp")]
    public async Task<ToolResult> McpAuthBearerAsync(
        [McpToolParameter("Authentication config name")] string auth_name,
        [McpToolParameter("Bearer Token")] string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(auth_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.AuthNameCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.TokenCannotBeEmpty)).Build();
        }

        try
        {
            var provider = new BearerAuthProvider(token);
            _authProviders[auth_name] = provider;

            await PersistAuthConfigAsync(auth_name, McpAuthConfigType.Bearer, cancellationToken).ConfigureAwait(false);

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.BearerTokenAuthConfigured, auth_name));
            response.AppendLine(L.T(StringKey.LabelTokenPrefix, token[..Math.Min(20, token.Length)]));

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ConfigureBearerTokenAuthFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConfigurationFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 配置 Basic 认证
    /// </summary>
    [McpTool(McpToolNameConstants.McpAuthBasic, "Configure Basic authentication for MCP server", "mcp")]
    public async Task<ToolResult> McpAuthBasicAsync(
        [McpToolParameter("Authentication config name")] string auth_name,
        [McpToolParameter("Username")] string username,
        [McpToolParameter("Password")] string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(auth_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.AuthNameCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.UsernameCannotBeEmpty)).Build();
        }

        try
        {
            var provider = new BasicAuthProvider(username, password);
            _authProviders[auth_name] = provider;

            await PersistAuthConfigAsync(auth_name, McpAuthConfigType.Basic, cancellationToken).ConfigureAwait(false);

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.BasicAuthConfigured, auth_name));
            response.AppendLine(L.T(StringKey.LabelUsername, username));

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ConfigureBasicAuthFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConfigurationFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 配置 OAuth2 认证
    /// </summary>
    [McpTool(McpToolNameConstants.McpAuthOAuth2, "Configure OAuth2 authentication for MCP server", "mcp")]
    public async Task<ToolResult> McpAuthOAuth2Async(
        [McpToolParameter("Authentication config name")] string auth_name,
        [McpToolParameter("Client ID")] string client_id,
        [McpToolParameter("Client secret")] string client_secret,
        [McpToolParameter("Token URL")] string token_url,
        [McpToolParameter("Authorization scopes (comma-separated)", Required = false)] string? scopes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(auth_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.AuthNameCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(client_id))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ClientIdCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(client_secret))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ClientSecretCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(token_url))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.TokenUrlCannotBeEmpty)).Build();
        }

        try
        {
            var scopeList = scopes?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList() ?? new List<string>();

            var provider = new OAuth2AuthProvider(new OAuth2ProviderOptions
            {
                ClientId = client_id,
                ClientSecret = client_secret,
                TokenUrl = token_url,
                Scopes = scopeList,
                HttpClient = _httpClientProvider?.GetClient(),
                Logger = _logger,
            });

            await _authLock.WaitAsync(cancellationToken);
            try
            {
                _authProviders[auth_name] = provider;
            }
            finally
            {
                _authLock.Release();
            }

            await PersistAuthConfigAsync(auth_name, McpAuthConfigType.OAuth2, cancellationToken).ConfigureAwait(false);

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.OAuth2AuthConfigured, auth_name));
            response.AppendLine(L.T(StringKey.LabelClientId, client_id));
            response.AppendLine(L.T(StringKey.LabelTokenUrl, token_url));

            if (scopeList.Count > 0)
            {
                response.AppendLine(L.T(StringKey.LabelScopes, string.Join(", ", scopeList)));
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ConfigureOAuth2AuthFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConfigurationFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 刷新认证令牌
    /// </summary>
    [McpTool(McpToolNameConstants.McpAuthRefresh, "Refresh MCP authentication token", "mcp")]
    public async Task<ToolResult> McpAuthRefreshAsync(
        [McpToolParameter("Authentication config name")] string auth_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(auth_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.AuthNameCannotBeEmpty)).Build();
        }

        await _authLock.WaitAsync(cancellationToken);
        try
        {
            if (!_authProviders.TryGetValue(auth_name, out var provider))
            {
                return McpResultBuilder.Error().WithText(L.T(StringKey.AuthConfigNotFound, auth_name)).Build();
            }

            var success = await provider.RefreshAsync(cancellationToken);

            if (success)
            {
                return McpResultBuilder.Success()
                    .WithText(L.T(StringKey.AuthTokenRefreshed, auth_name))
                    .Build();
            }
            else
            {
                return McpResultBuilder.Error()
                    .WithText(L.T(StringKey.RefreshAuthTokenFailed, auth_name))
                    .Build();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.RefreshAuthTokenFailedLog), auth_name);
            return McpResultBuilder.Error().WithText(L.T(StringKey.RefreshFailed, ex.Message)).Build();
        }
        finally
        {
            _authLock.Release();
        }
    }

    /// <summary>
    /// 获取认证状态
    /// </summary>
    [McpTool(McpToolNameConstants.McpAuthStatus, "Get MCP authentication status", "mcp")]
    public Task<ToolResult> McpAuthStatusAsync(
        [McpToolParameter("Authentication config name", Required = false)] string? auth_name = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = new System.Text.StringBuilder();

            if (string.IsNullOrWhiteSpace(auth_name))
            {
                response.AppendLine(L.T(StringKey.AllAuthConfigsStatus));
                response.AppendLine();

                if (_authProviders.Count == 0)
                {
                    response.AppendLine(L.T(StringKey.NoAuthConfigs));
                }
                else
                {
                    foreach (var (name, provider) in _authProviders)
                    {
                        response.AppendLine($"- {name}");
                        response.AppendLine($"  {L.T(StringKey.LabelType, provider.AuthType)}");
                        response.AppendLine($"  {L.T(StringKey.LabelStatus, provider.IsAuthenticated ? L.T(StringKey.Authenticated) : L.T(StringKey.NotAuthenticated))}");
                        response.AppendLine();
                    }
                }
            }
            else
            {
                if (!_authProviders.TryGetValue(auth_name, out var provider))
                {
                    return Task.FromResult(McpResultBuilder.Error()
                        .WithText(L.T(StringKey.AuthConfigNotFound, auth_name))
                        .Build());
                }

                response.AppendLine(L.T(StringKey.LabelAuthConfig, auth_name));
                response.AppendLine(L.T(StringKey.LabelType, provider.AuthType));
                response.AppendLine(L.T(StringKey.LabelStatus, provider.IsAuthenticated ? L.T(StringKey.Authenticated) : L.T(StringKey.NotAuthenticated)));
            }

            return Task.FromResult(McpResultBuilder.Success().WithText(response.ToString()).Build());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.GetAuthStatusFailedLog));
            return Task.FromResult(McpResultBuilder.Error().WithText(L.T(StringKey.GetStatusFailed, ex.Message)).Build());
        }
    }

    /// <summary>
    /// 删除认证配置
    /// </summary>
    [McpTool(McpToolNameConstants.McpAuthRemove, "Delete MCP authentication config", "mcp")]
    public Task<ToolResult> McpAuthRemoveAsync(
        [McpToolParameter("Authentication config name")] string auth_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(auth_name))
        {
            return Task.FromResult(McpResultBuilder.Error().WithText(L.T(StringKey.AuthNameCannotBeEmpty)).Build());
        }

        try
        {
            if (!_authProviders.Remove(auth_name))
            {
                return Task.FromResult(McpResultBuilder.Error()
                    .WithText(L.T(StringKey.AuthConfigNotFound, auth_name))
                    .Build());
            }

            _ = RemovePersistedAuthConfigAsync(auth_name, cancellationToken);

            return Task.FromResult(McpResultBuilder.Success()
                .WithText(L.T(StringKey.AuthConfigRemoved, auth_name))
                .Build());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.RemoveAuthConfigFailedLog));
            return Task.FromResult(McpResultBuilder.Error().WithText(L.T(StringKey.RemoveFailed, ex.Message)).Build());
        }
    }

    /// <summary>
    /// 获取认证提供者
    /// </summary>
    public IMcpAuthProvider? GetAuthProvider(string authName)
    {
        _authProviders.TryGetValue(authName, out var provider);
        return provider;
    }

    /// <summary>
    /// 根据认证名称获取 McpAuthConfig — 用于 Agent 内联 MCP 服务器认证
    /// </summary>
    public McpAuthConfig? GetAuthConfig(string authName)
    {
        if (!_authProviders.TryGetValue(authName, out var provider))
            return null;

        return provider.AuthType switch
        {
            McpAuthType.ApiKey => new McpAuthConfig
            {
                Type = McpAuthType.ApiKey,
                ApiKey = provider is ApiKeyAuthProvider keyProvider ? keyProvider.ApiKey : null
            },
            McpAuthType.Bearer => new McpAuthConfig
            {
                Type = McpAuthType.Bearer,
                BearerToken = provider is BearerAuthProvider bearerProvider ? bearerProvider.Token : null
            },
            McpAuthType.Basic => new McpAuthConfig
            {
                Type = McpAuthType.Basic,
                Username = provider is BasicAuthProvider basicProvider ? basicProvider.Username : null,
                Password = provider is BasicAuthProvider basicPwdProvider ? basicPwdProvider.Password : null
            },
            _ => null
        };
    }

    private async Task PersistAuthConfigAsync(string authName, McpAuthConfigType authType, CancellationToken ct)
    {
        if (_persistenceService == null) return;
        try
        {
            if (_authProviders.TryGetValue(authName, out var provider))
            {
                await _persistenceService.SaveAsync(authName, authType.ToValue(), provider.AuthType.ToString(), ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.PersistAuthConfigFailedLog), authName);
        }
    }

    private async Task RemovePersistedAuthConfigAsync(string authName, CancellationToken ct)
    {
        if (_persistenceService == null) return;
        try
        {
            await _persistenceService.RemoveAsync(authName, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.RemovePersistedAuthConfigFailedLog), authName);
        }
    }

    public void Dispose()
    {
        foreach (var provider in _authProviders.Values.OfType<IDisposable>())
        {
            try
            {
                provider.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, L.T(StringKey.DisposeAuthProviderErrorLog));
            }
        }
        _authProviders.Clear();

        try
        {
            _authLock.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.DisposeTimeoutOrFailedLog));
        }
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _authProviders.Values.OfType<IAsyncDisposable>())
        {
            await provider.DisposeAsync();
        }
        _authProviders.Clear();
        _authLock.Dispose();
    }
}
