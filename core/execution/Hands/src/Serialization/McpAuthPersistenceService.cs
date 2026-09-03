namespace IO.Services;

[Register(typeof(IMcpAuthPersistenceService), ServiceLifetime.Singleton)]
public sealed partial class McpAuthPersistenceService : ServiceEntity, IMcpAuthPersistenceService
{
    private readonly IConfigurationService? _configService;
    private readonly ILogger<McpAuthPersistenceService>? _logger;
    private readonly AsyncLock _lock = new();

    public McpAuthPersistenceService(IConfigurationService? configService = null, ILogger<McpAuthPersistenceService>? logger = null)
    {
        _configService = configService;
        _logger = logger;
    }

    public async Task SaveAsync(string authName, string authType, string serializedData, CancellationToken ct = default)
    {
        if (_configService == null) return;

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException("锁等待超时");

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

    public async Task<AuthConfigEntry?> LoadAsync(string authName, CancellationToken ct = default)
    {
        if (_configService == null) return null;

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException("锁等待超时");

        var entries = await LoadEntriesAsync(ct).ConfigureAwait(false);
        return entries.TryGetValue(authName, out var entry) ? entry : null;
    
    }

    public async Task<IReadOnlyList<AuthConfigEntry>> ListAsync(CancellationToken ct = default)
    {
        if (_configService == null) return Array.Empty<AuthConfigEntry>();

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException("锁等待超时");

        return (await LoadEntriesAsync(ct).ConfigureAwait(false)).Values.ToList();
    
    }

    public async Task RemoveAsync(string authName, CancellationToken ct = default)
    {
        if (_configService == null) return;

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException("锁等待超时");

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

    protected override void OnDispose() => _lock.Dispose();
}

