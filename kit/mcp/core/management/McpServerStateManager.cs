
namespace McpClient;

/// <summary>
/// MCP 服务器状态管理器 — 维护禁用/启用状态并持久化到磁盘
/// </summary>
public sealed partial class McpServerStateManager
{
    private readonly IFileSystem _fs;
    private readonly ILogger<McpServerStateManager>? _logger;
    private readonly string _stateFilePath;
    private readonly AsyncLock _lock = new();
    private HashSet<string> _disabledServers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化 MCP 服务器状态管理器
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="stateFilePath">状态文件路径</param>
    /// <param name="logger">日志记录器（可选）</param>
    public McpServerStateManager(IFileSystem fs, string stateFilePath, ILogger<McpServerStateManager>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateFilePath);
        _fs = fs;
        _stateFilePath = stateFilePath;
        _logger = logger;
    }

    /// <summary>
    /// 异步从磁盘加载已持久化的禁用服务器列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_fs.FileExists(_stateFilePath))
            {
                _logger?.LogDebug("MCP 服务器状态文件不存在: {Path}", _stateFilePath);
                return;
            }

            var state = await _fs.ReadAndDeserializeAsync(_stateFilePath, McpClientJsonContext.Default.McpServerDisabledState, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// 判断指定服务器是否处于禁用状态
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <returns>若已禁用返回 true；否则 false</returns>
    public bool IsDisabled(string serverName)
    {
        return _disabledServers.Contains(serverName);
    }

    /// <summary>
    /// 异步禁用指定 MCP 服务器并持久化状态
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>若状态发生变更（由启用变为禁用）返回 true；若原本已禁用返回 false</returns>
    public async Task<bool> DisableAsync(string serverName, CancellationToken cancellationToken = default)
    {
        bool changed;
        using (var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            changed = _disabledServers.Add(serverName);
        }

        if (!changed) return false;

        await PersistAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("MCP 服务器 {ServerName} 已禁用", serverName);
        return true;

    }

    /// <summary>
    /// 异步启用指定 MCP 服务器并持久化状态
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>若状态发生变更（由禁用变为启用）返回 true；若原本已启用返回 false</returns>
    public async Task<bool> EnableAsync(string serverName, CancellationToken cancellationToken = default)
    {
        bool changed;
        using (var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            changed = _disabledServers.Remove(serverName);
        }

        if (!changed) return false;

        await PersistAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("MCP 服务器 {ServerName} 已启用", serverName);
        return true;

    }

    /// <summary>
    /// 获取当前所有被禁用的服务器名称集合
    /// </summary>
    /// <returns>禁用服务器名称的只读集合</returns>
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

            var json = RelaxedJsonSerializer.Serialize(state, McpClientJsonContext.Default);
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

/// <summary>
/// MCP 服务器禁用状态持久化 DTO
/// </summary>
public sealed partial class McpServerDisabledState
{
    /// <summary>被禁用的服务器名称列表</summary>
    [JsonPropertyName("disabled_servers")]
    public List<string> DisabledServers { get; set; } = new();
}