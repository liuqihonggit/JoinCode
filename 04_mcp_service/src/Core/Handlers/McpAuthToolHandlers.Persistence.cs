namespace McpToolDispatch;

/// <summary>
/// McpAuthToolHandlers 持久化方法 — partial class，分离文件 IO 逻辑。
/// 序列化到 ~/.jcc/mcp/auth.json，支持 CLI 无状态模式跨进程共享认证配置。
/// 包含敏感信息（API Key/Token/Password），因为 ~/.jcc/ 是用户私有目录。
/// </summary>
public sealed partial class McpAuthToolHandlers
{
    private readonly IFileSystem? _authPersistenceFs;
    private readonly string? _authStateFilePath;

    /// <summary>
    /// 获取 MCP 认证状态文件路径: ~/.jcc/mcp/auth.json
    /// </summary>
    private static string GetAuthStateFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder,
            AppDataConstants.McpFolderName,
            AppDataConstants.McpAuthFileName);
    }

    /// <summary>
    /// 从磁盘加载 MCP 认证配置。文件不存在或读取失败时静默跳过。
    /// </summary>
    private void LoadAuthState()
    {
        if (_authPersistenceFs is null || _authStateFilePath is null) return;

        try
        {
            if (!_authPersistenceFs.FileExists(_authStateFilePath)) return;

            var json = _authPersistenceFs.ReadAllText(_authStateFilePath);
            if (string.IsNullOrWhiteSpace(json)) return;

            var data = RelaxedJsonSerializer.Deserialize(json, McpClientJsonContext.Default.McpAuthStateData);
            if (data?.AuthConfigs is null) return;

            foreach (var entry in data.AuthConfigs)
            {
                RestoreAuthProvider(entry);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载 MCP 认证状态失败，将使用空状态启动");
        }
    }

    /// <summary>
    /// 根据持久化记录恢复认证提供者到内存字典。
    /// </summary>
    private void RestoreAuthProvider(McpAuthEntry entry)
    {
        IMcpAuthProvider? provider = null;

        switch (entry.AuthType)
        {
            case "ApiKey":
                if (!string.IsNullOrEmpty(entry.ApiKey))
                    provider = new ApiKeyAuthProvider(entry.ApiKey, entry.HeaderName ?? "X-API-Key");
                break;

            case "Bearer":
                if (!string.IsNullOrEmpty(entry.Token))
                    provider = new BearerAuthProvider(entry.Token);
                break;

            case "Basic":
                if (!string.IsNullOrEmpty(entry.Username) && entry.Password is not null)
                    provider = new BasicAuthProvider(entry.Username, entry.Password);
                break;

            case "OAuth2":
                if (!string.IsNullOrEmpty(entry.ClientId) && !string.IsNullOrEmpty(entry.ClientSecret) && !string.IsNullOrEmpty(entry.TokenUrl))
                {
                    provider = new OAuth2AuthProvider(new OAuth2ProviderOptions
                    {
                        ClientId = entry.ClientId,
                        ClientSecret = entry.ClientSecret,
                        TokenUrl = entry.TokenUrl,
                        Scopes = entry.Scopes ?? new List<string>(),
                        HttpClient = _httpClientProvider?.GetClient(),
                        Logger = _logger,
                    });
                }
                break;
        }

        if (provider is not null)
        {
            _authProviders[entry.AuthName] = provider;
        }
    }

    /// <summary>
    /// 保存单条认证配置到磁盘（增量更新）。
    /// </summary>
    private async Task SaveAuthEntryAsync(McpAuthEntry entry, CancellationToken ct)
    {
        if (_authPersistenceFs is null || _authStateFilePath is null) return;

        try
        {
            var data = LoadAuthStateData() ?? new McpAuthStateData();
            var existing = data.AuthConfigs.FirstOrDefault(x => x.AuthName == entry.AuthName);
            if (existing is not null)
            {
                data.AuthConfigs.Remove(existing);
            }
            data.AuthConfigs.Add(entry);

            await WriteAuthStateDataAsync(data, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存 MCP 认证状态失败");
        }
    }

    /// <summary>
    /// 从磁盘移除单条认证配置。
    /// </summary>
    private async Task RemoveAuthEntryAsync(string authName, CancellationToken ct)
    {
        if (_authPersistenceFs is null || _authStateFilePath is null) return;

        try
        {
            var data = LoadAuthStateData();
            if (data is null) return;

            var existing = data.AuthConfigs.FirstOrDefault(x => x.AuthName == authName);
            if (existing is not null)
            {
                data.AuthConfigs.Remove(existing);
                await WriteAuthStateDataAsync(data, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "移除 MCP 认证状态失败");
        }
    }

    /// <summary>
    /// 保存当前所有认证配置到磁盘（全量覆盖）。
    /// </summary>
    private async Task SaveAuthStateAsync(CancellationToken ct)
    {
        if (_authPersistenceFs is null || _authStateFilePath is null) return;

        try
        {
            var entries = new List<McpAuthEntry>();
            foreach (var (name, provider) in _authProviders)
            {
                var entry = BuildAuthEntryFromProvider(name, provider);
                if (entry is not null) entries.Add(entry);
            }

            var data = new McpAuthStateData { AuthConfigs = entries };
            await WriteAuthStateDataAsync(data, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存 MCP 认证状态失败");
        }
    }

    /// <summary>
    /// 从内存 provider 构建持久化记录（仅限可提取属性的类型）。
    /// OAuth2 因属性私有，需在各方法中直接构建 entry 保存。
    /// </summary>
    private static McpAuthEntry? BuildAuthEntryFromProvider(string name, IMcpAuthProvider provider)
    {
        var entry = new McpAuthEntry
        {
            AuthName = name,
            AuthType = provider.AuthType.ToString(),
        };

        switch (provider)
        {
            case ApiKeyAuthProvider keyProvider:
                entry.ApiKey = keyProvider.ApiKey;
                entry.HeaderName = keyProvider.HeaderName;
                return entry;

            case BearerAuthProvider bearerProvider:
                entry.Token = bearerProvider.Token;
                return entry;

            case BasicAuthProvider basicProvider:
                entry.Username = basicProvider.Username;
                entry.Password = basicProvider.Password;
                return entry;

            default:
                return null;
        }
    }

    private McpAuthStateData? LoadAuthStateData()
    {
        if (_authPersistenceFs is null || _authStateFilePath is null) return null;
        if (!_authPersistenceFs.FileExists(_authStateFilePath)) return null;

        var json = _authPersistenceFs.ReadAllText(_authStateFilePath);
        if (string.IsNullOrWhiteSpace(json)) return null;

        return RelaxedJsonSerializer.Deserialize(json, McpClientJsonContext.Default.McpAuthStateData);
    }

    private async Task WriteAuthStateDataAsync(McpAuthStateData data, CancellationToken ct)
    {
        if (_authPersistenceFs is null || _authStateFilePath is null) return;

        var json = RelaxedJsonSerializer.Serialize(data, McpClientJsonContext.Default);
        var dir = Path.GetDirectoryName(_authStateFilePath);
        if (!string.IsNullOrEmpty(dir) && !_authPersistenceFs.DirectoryExists(dir))
        {
            _authPersistenceFs.CreateDirectory(dir);
        }

        _authPersistenceFs.WriteAllText(_authStateFilePath, json);
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
