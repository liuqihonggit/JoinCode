namespace IO.Services;

/// <summary>
/// MCP 认证持久化服务 — 负责将 MCP 认证条目（名称、类型、序列化数据）持久化到配置存储，
/// 并通过异步锁保证并发读写安全。
/// </summary>
[Register(typeof(IMcpAuthPersistenceService), ServiceLifetime.Singleton)]
public sealed partial class McpAuthPersistenceService : ServiceEntity, IMcpAuthPersistenceService
{
    private readonly IConfigurationService? _configService;
    private readonly ILogger<McpAuthPersistenceService>? _logger;
    private readonly AsyncLock _lock = new();

    /// <summary>
    /// 构造 MCP 认证持久化服务。
    /// </summary>
    /// <param name="configService">配置服务（可选，为 null 时所有操作变为空操作）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public McpAuthPersistenceService(IConfigurationService? configService = null, ILogger<McpAuthPersistenceService>? logger = null)
    {
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// 异步保存一条 MCP 认证条目。若同名条目已存在则覆盖。
    /// </summary>
    /// <param name="authName">认证名称（唯一键）。</param>
    /// <param name="authType">认证类型。</param>
    /// <param name="serializedData">序列化后的认证数据。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示异步保存操作的任务。</returns>
    public async Task SaveAsync(string authName, string authType, string serializedData, CancellationToken ct = default)
    {
        if (_configService == null) return;

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        var entries = await LoadEntriesAsync(ct).ConfigureAwait(false);
        var entry = new AuthConfigEntry
        {
            Name = authName,
            AuthType = authType,
            Data = serializedData,
            SavedAt = DateTime.UtcNow
        };

        entries[authName] = entry;

        await SaveEntriesAsync(entries, ct).ConfigureAwait(false);
    
    }

    /// <summary>
    /// 异步加载指定名称的 MCP 认证条目。
    /// </summary>
    /// <param name="authName">认证名称。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>匹配的认证条目；若不存在则返回 <c>null</c>。</returns>
    public async Task<AuthConfigEntry?> LoadAsync(string authName, CancellationToken ct = default)
    {
        if (_configService == null) return null;

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        var entries = await LoadEntriesAsync(ct).ConfigureAwait(false);
        return entries.TryGetValue(authName, out var entry) ? entry : null;
    
    }

    /// <summary>
    /// 异步列出全部 MCP 认证条目。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>认证条目只读列表；若无任何条目则返回空列表。</returns>
    public async Task<IReadOnlyList<AuthConfigEntry>> ListAsync(CancellationToken ct = default)
    {
        if (_configService == null) return Array.Empty<AuthConfigEntry>();

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        return (await LoadEntriesAsync(ct).ConfigureAwait(false)).Values.ToList();
    
    }

    /// <summary>
    /// 异步移除指定名称的 MCP 认证条目。若条目不存在则静默忽略。
    /// </summary>
    /// <param name="authName">认证名称。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示异步移除操作的任务。</returns>
    public async Task RemoveAsync(string authName, CancellationToken ct = default)
    {
        if (_configService == null) return;

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        var entries = await LoadEntriesAsync(ct).ConfigureAwait(false);
        entries.Remove(authName);
        await SaveEntriesAsync(entries, ct).ConfigureAwait(false);
    
    }

    private async Task<Dictionary<string, AuthConfigEntry>> LoadEntriesAsync(CancellationToken ct)
    {
        var configService = _configService ?? throw new InvalidOperationException("Config service not available.");
        try
        {
            var json = await configService.GetAsync("mcp.auth_entries", ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, AuthConfigEntry>();

            var entries = RelaxedJsonSerializer.Deserialize(json, AuthEntryContext.Default.ListAuthConfigEntry);
            if (entries == null) return new Dictionary<string, AuthConfigEntry>();
            return entries.ToDictionary(e => e.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载 MCP 认证配置失败");
            return new Dictionary<string, AuthConfigEntry>();
        }
    }

    private async Task SaveEntriesAsync(Dictionary<string, AuthConfigEntry> entries, CancellationToken ct)
    {
        var configService = _configService ?? throw new InvalidOperationException("Config service not available.");
        try
        {
            var list = entries.Values.ToList();
            var json = RelaxedJsonSerializer.Serialize(list, AuthEntryContext.Default);
            await configService.SetAsync("mcp.auth_entries", json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存 MCP 认证配置失败");
        }
    }

    /// <summary>
    /// 释放异步锁持有的资源。
    /// </summary>
    public override void Dispose()
    {
        _lock.Dispose();
        base.Dispose();
    }
}

