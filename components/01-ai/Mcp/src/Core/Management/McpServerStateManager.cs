
namespace McpClient;

public sealed partial class McpServerStateManager
{
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<McpServerStateManager>? _logger;
    private readonly string _stateFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private HashSet<string> _disabledServers = new(StringComparer.OrdinalIgnoreCase);

    public McpServerStateManager(IFileSystem fs, string stateFilePath, ILogger<McpServerStateManager>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateFilePath);
        _fs = fs;
        _stateFilePath = stateFilePath;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_fs.FileExists(_stateFilePath))
            {
                _logger?.LogDebug("MCP 服务器状态文件不存在: {Path}", _stateFilePath);
                return;
            }

            var json = await _fs.ReadAllTextAsync(_stateFilePath, cancellationToken).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize(json, McpClientJsonContext.Default.McpServerDisabledState);
            if (state?.DisabledServers != null)
            {
                _disabledServers = new HashSet<string>(state.DisabledServers, StringComparer.OrdinalIgnoreCase);
            }

            _logger?.LogInformation("已加载 {Count} 个禁用的 MCP 服务器", _disabledServers.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载 MCP 服务器状态失败");
        }
    }

    public bool IsDisabled(string serverName)
    {
        return _disabledServers.Contains(serverName);
    }

    public async Task<bool> DisableAsync(string serverName, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_disabledServers.Add(serverName))
            {
                return false;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("MCP 服务器 {ServerName} 已禁用", serverName);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> EnableAsync(string serverName, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_disabledServers.Remove(serverName))
            {
                return false;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("MCP 服务器 {ServerName} 已启用", serverName);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public IReadOnlySet<string> GetDisabledServers()
    {
        return _disabledServers;
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = new McpServerDisabledState
            {
                DisabledServers = _disabledServers.ToList()
            };

            var json = JsonSerializer.Serialize(state, McpClientJsonContext.Default.McpServerDisabledState);
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                DirectoryHelper.EnsureDirectoryExists(_fs, directory);
            }

            await _fs.WriteAllTextAsync(_stateFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "持久化 MCP 服务器状态失败");
        }
    }
}

public sealed partial class McpServerDisabledState
{
    [JsonPropertyName("disabled_servers")]
    public List<string> DisabledServers { get; set; } = new();
}