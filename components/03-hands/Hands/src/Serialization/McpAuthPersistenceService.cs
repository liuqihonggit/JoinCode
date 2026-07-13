using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class McpAuthPersistenceService : IMcpAuthPersistenceService
{
    private readonly IConfigurationService? _configService;
    [Inject] private readonly ILogger<McpAuthPersistenceService>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public McpAuthPersistenceService(IConfigurationService? configService = null, ILogger<McpAuthPersistenceService>? logger = null)
    {
        _configService = configService;
        _logger = logger;
    }

    public async Task SaveAsync(string authName, string authType, string serializedData, CancellationToken ct = default)
    {
        if (_configService == null) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadEntriesAsync(ct).ConfigureAwait(false);
            var existing = entries.FindIndex(e => e.Name == authName);
            var entry = new AuthConfigEntry
            {
                Name = authName,
                AuthType = authType,
                Data = serializedData,
                SavedAt = DateTime.UtcNow
            };

            if (existing >= 0)
                entries[existing] = entry;
            else
                entries.Add(entry);

            await SaveEntriesAsync(entries, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AuthConfigEntry?> LoadAsync(string authName, CancellationToken ct = default)
    {
        if (_configService == null) return null;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadEntriesAsync(ct).ConfigureAwait(false);
            return entries.Find(e => e.Name == authName);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<AuthConfigEntry>> ListAsync(CancellationToken ct = default)
    {
        if (_configService == null) return Array.Empty<AuthConfigEntry>();

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadEntriesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(string authName, CancellationToken ct = default)
    {
        if (_configService == null) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadEntriesAsync(ct).ConfigureAwait(false);
            entries.RemoveAll(e => e.Name == authName);
            await SaveEntriesAsync(entries, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<AuthConfigEntry>> LoadEntriesAsync(CancellationToken ct)
    {
        try
        {
            var json = await _configService!.GetAsync("mcp.auth_entries", ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json)) return [];

            var entries = JsonSerializer.Deserialize(json, AuthEntryContext.Default.ListAuthConfigEntry);
            return entries ?? [];
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载 MCP 认证配置失败");
            return [];
        }
    }

    private async Task SaveEntriesAsync(List<AuthConfigEntry> entries, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(entries, AuthEntryContext.Default.ListAuthConfigEntry);
            await _configService!.SetAsync("mcp.auth_entries", json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存 MCP 认证配置失败");
        }
    }
}

