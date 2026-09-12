namespace McpToolDispatch;

/// <summary>
/// McpClientToolHandlers 持久化方法 — partial class，分离文件 IO 逻辑以控制文件长度。
/// 序列化到 ~/.jcc/mcp/connections.json，支持 CLI 无状态模式跨进程共享 MCP 连接配置。
/// 只持久化连接配置（endpoint/transport/auth_name），不持久化敏感凭证（token/password）。
/// 启动时根据配置重新建立连接。
/// </summary>
public sealed partial class McpClientToolHandlers
{
    private readonly IFileSystem? _persistenceFs;
    private readonly string? _stateFilePath;
    private readonly ConcurrentDictionary<string, McpConnectionEntry> _connectionConfigs = new();
    private Task? _restoreTask;
    private CancellationTokenSource? _restoreCts;
    private volatile bool _isRestoring;

    /// <summary>
    /// 获取 MCP 连接状态文件路径: ~/.jcc/mcp/connections.json
    /// </summary>
    private static string GetStateFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder,
            AppDataConstants.McpFolderName,
            AppDataConstants.McpConnectionsFileName);
    }

    /// <summary>
    /// 从磁盘加载 MCP 连接配置。文件不存在或读取失败时静默跳过（不影响启动）。
    /// 只读取配置到内存列表，不建立连接 — 连接由 RestoreConnectionsAsync 异步完成。
    /// </summary>
    private List<McpConnectionEntry>? LoadState()
    {
        if (_persistenceFs is null || _stateFilePath is null) return null;

        try
        {
            if (!_persistenceFs.FileExists(_stateFilePath)) return null;

            var json = _persistenceFs.ReadAllText(_stateFilePath);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var data = RelaxedJsonSerializer.Deserialize(json, McpClientJsonContext.Default.McpConnectionStateData);
            return data?.Connections;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载 MCP 连接状态失败，将使用空状态启动");
            return null;
        }
    }

    /// <summary>
    /// 异步恢复持久化的 MCP 连接 — 遍历配置列表逐个调 McpConnectAsync 重建连接。
    /// 连接失败静默跳过（不影响其他连接和后续操作）。
    /// 在构造函数中 fire-and-forget 启动，GetClientAsync 中 await _restoreTask 确保恢复完成。
    /// </summary>
    private async Task RestoreConnectionsAsync(List<McpConnectionEntry>? entries)
    {
        if (entries is null || entries.Count == 0) return;

        _isRestoring = true;
        try
        {
            foreach (var entry in entries)
            {
                if (_restoreCts?.IsCancellationRequested == true) return;

                try
                {
                    var token = _restoreCts?.Token ?? default;
                    var result = await McpConnectAsync(
                        entry.Name,
                        entry.Endpoint,
                        entry.TransportType,
                        entry.UseOAuth,
                        entry.AuthName,
                        token).ConfigureAwait(false);

                    if (result.IsError)
                    {
                        _logger?.LogWarning("恢复 MCP 连接 '{Name}' 失败，跳过", entry.Name);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogWarning(ex, "恢复 MCP 连接 '{Name}' 异常，跳过", entry.Name);
                }
            }

            _logger?.LogDebug("MCP 连接恢复完成，共恢复 {Count} 个连接", _clients.Count);
        }
        finally
        {
            _isRestoring = false;
        }
    }

    /// <summary>
    /// 保存 MCP 连接配置到磁盘。写入失败时静默跳过（不影响操作结果）。
    /// 只持久化连接配置，不持久化敏感凭证（BearerToken/ApiKey/Password）。
    /// 无锁读取 _connectionConfigs（ConcurrentDictionary 线程安全枚举），可在 _clientLock 锁内调用。
    /// </summary>
    private async Task SaveStateAsync(CancellationToken cancellationToken = default)
    {
        if (_persistenceFs is null || _stateFilePath is null) return;

        try
        {
            var configs = _connectionConfigs.Values.ToList();
            var data = new McpConnectionStateData { Connections = configs };

            var dir = Path.GetDirectoryName(_stateFilePath);
            if (dir is not null && !_persistenceFs.DirectoryExists(dir))
            {
                _persistenceFs.CreateDirectory(dir);
            }

            var json = RelaxedJsonSerializer.Serialize(data, McpClientJsonContext.Default);
            await _persistenceFs.WriteAllTextAsync(_stateFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "保存 MCP 连接状态失败");
        }
    }

    /// <summary>
    /// 等待启动时连接恢复完成 — 在 GetClientAsync 中调用，确保恢复完成后再查询连接。
    /// 恢复任务为 null（无持久化文件系统）或已完成时立即返回。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Threading", "VSTHRD003:Avoid awaiting foreign tasks", Justification = "后台恢复任务在构造函数启动，GetClientAsync 中 await 是安全的，非 UI 线程无 SynchronizationContext")]
    private async Task WaitForRestoreAsync()
    {
        if (_restoreTask is not null)
        {
            try
            {
                await _restoreTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "等待 MCP 连接恢复任务结束时异常");
            }
        }
    }
}
