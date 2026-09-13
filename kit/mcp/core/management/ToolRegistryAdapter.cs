namespace McpToolRegistry;

/// <summary>
/// MCP 工具注册表适配器 — 桥接 IToolRegistry 与 RemoteClientManager，实现 IMcpToolRegistry
/// </summary>
[Register(typeof(IMcpToolRegistry), ServiceLifetime.Singleton)]
public sealed partial class ToolRegistryAdapter : IMcpToolRegistry
{
    private readonly IToolRegistry _toolRegistry;
    private readonly RemoteClientManager _remoteClientManager;
    private readonly ILogger<ToolRegistryAdapter>? _logger;

    /// <summary>
    /// 初始化工具注册表适配器
    /// </summary>
    /// <param name="toolRegistry">底层工具注册表</param>
    /// <param name="remoteClientManager">远程客户端管理器</param>
    /// <param name="logger">日志记录器（可选）</param>
    public ToolRegistryAdapter(
        IToolRegistry toolRegistry,
        RemoteClientManager remoteClientManager,
        ILogger<ToolRegistryAdapter>? logger = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _remoteClientManager = remoteClientManager ?? throw new ArgumentNullException(nameof(remoteClientManager));
        _logger = logger;
    }

    #region Local Tool Registration

    /// <summary>
    /// 异步注册工具处理器
    /// </summary>
    /// <param name="handler">工具处理器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task RegisterToolAsync(IToolHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        await _toolRegistry.RegisterToolAsync(handler, cancellationToken);

        _logger?.LogDebug("MCP tool registered via adapter: {ToolName}", handler.Name);
    }

    /// <summary>
    /// 异步注册工具（以委托形式提供处理逻辑）
    /// </summary>
    /// <param name="name">工具名称</param>
    /// <param name="description">工具描述</param>
    /// <param name="inputSchema">输入参数 schema</param>
    /// <param name="handler">工具处理委托</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="kind">工具种类（默认 System）</param>
    /// <param name="groupName">工具分组名称（可选）</param>
    /// <param name="timeoutPolicy">超时策略（可选）</param>
    /// <param name="category">工具分类（可选）</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default, ToolKind kind = ToolKind.System, string? groupName = null, ToolTimeoutPolicy? timeoutPolicy = null, string? category = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(handler);

        var delegateHandler = new DelegateToolHandler(name, description, inputSchema, handler, kind, groupName, timeoutPolicy, category);
        await _toolRegistry.RegisterToolAsync(delegateHandler, cancellationToken);
    }

    /// <summary>
    /// 异步注销指定工具
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>若注销成功返回 true；否则 false</returns>
    public Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        return _toolRegistry.UnregisterToolAsync(toolName, cancellationToken);
    }

    #endregion

    #region Remote MCP Client Management

    /// <summary>
    /// 注册远程 MCP 客户端（fire-and-forget 异步注册）
    /// </summary>
    /// <param name="clientId">客户端标识</param>
    /// <param name="client">MCP 客户端实例</param>
    public void RegisterRemoteClient(string clientId, IMcpClient client)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentNullException.ThrowIfNull(client);

        _ = _remoteClientManager.RegisterClientAsync(clientId, client, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步注销远程 MCP 客户端
    /// </summary>
    /// <param name="clientId">客户端标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>若注销成功返回 true；否则 false</returns>
    public Task<bool> UnregisterRemoteClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        return _remoteClientManager.UnregisterClientAsync(clientId, cancellationToken);
    }

    /// <summary>
    /// 异步获取指定远程 MCP 客户端
    /// </summary>
    /// <param name="clientId">客户端标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>MCP 客户端实例；若不存在返回 null</returns>
    public Task<IMcpClient?> GetRemoteClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        return _remoteClientManager.GetClientAsync(clientId, cancellationToken);
    }

    /// <summary>
    /// 异步获取所有远程 MCP 客户端
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>以客户端标识为键的 MCP 客户端只读字典</returns>
    public Task<IReadOnlyDictionary<string, IMcpClient>> GetAllRemoteClientsAsync(CancellationToken cancellationToken = default)
    {
        return _remoteClientManager.GetAllClientsAsync(cancellationToken);
    }

    /// <summary>
    /// 异步同步远程 MCP 客户端暴露的工具到本地注册表
    /// </summary>
    /// <param name="clientId">客户端标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>远程工具同步结果</returns>
    public Task<RemoteToolsSyncResult> SyncRemoteToolsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        return _remoteClientManager.SyncToolsAsync(clientId, cancellationToken);
    }

    #endregion

    #region Tool Execution

    /// <summary>
    /// 异步获取指定工具的处理器
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具处理器；若不存在返回 null</returns>
    public Task<IToolHandler?> GetToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetToolAsync(toolName, cancellationToken);
    }

    /// <summary>
    /// 异步获取所有已注册工具的处理器字典
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>以工具名为键的处理器只读字典</returns>
    public async Task<IReadOnlyDictionary<string, IToolHandler>> GetAllToolsAsync(CancellationToken cancellationToken = default)
    {
        return await _toolRegistry.GetAllToolsAsync(cancellationToken);
    }

    /// <summary>
    /// 异步执行指定工具
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数字典</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="onProgress">进度回调（可选）</param>
    /// <returns>工具执行结果</returns>
    public Task<ToolResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        return _toolRegistry.ExecuteToolAsync(toolName, arguments, cancellationToken, onProgress);
    }

    /// <summary>
    /// 异步获取指定工具的元信息
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具元信息；若不存在返回 null</returns>
    public Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetToolInfoAsync(toolName, cancellationToken);
    }

    /// <summary>
    /// 异步获取所有工具的元信息列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具元信息只读列表</returns>
    public Task<IReadOnlyList<ToolInfo>> GetAllToolInfosAsync(CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetAllToolInfosAsync(cancellationToken);
    }

    /// <summary>
    /// 异步判断指定工具是否已注册
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>若已注册返回 true；否则 false</returns>
    public Task<bool> ContainsToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        return _toolRegistry.ContainsToolAsync(toolName, cancellationToken);
    }

    /// <summary>
    /// 异步获取所有工具分组名称
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分组名称的冻结集合</returns>
    public Task<FrozenSet<string>> GetGroupNamesAsync(CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetGroupNamesAsync(cancellationToken);
    }

    /// <summary>
    /// 异步按工具种类获取工具处理器字典
    /// </summary>
    /// <param name="kind">工具种类</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>以工具名为键的处理器只读字典</returns>
    public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByKindAsync(ToolKind kind, CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetToolsByKindAsync(kind, cancellationToken);
    }

    /// <summary>
    /// 异步按分组名称获取工具处理器字典
    /// </summary>
    /// <param name="groupName">分组名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>以工具名为键的处理器只读字典</returns>
    public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByGroupAsync(string groupName, CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetToolsByGroupAsync(groupName, cancellationToken);
    }

    #endregion

    #region Cache Management

    /// <summary>
    /// 清除远程客户端缓存
    /// </summary>
    public void ClearCache()
    {
        _remoteClientManager.ClearCache();
    }

    #endregion

    #region Statistics

    /// <summary>
    /// 异步获取本地已注册工具数量
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>本地工具数量</returns>
    public Task<int> GetLocalToolCountAsync(CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetCountAsync(cancellationToken);
    }

    /// <summary>
    /// 异步获取远程 MCP 客户端数量
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>远程客户端数量</returns>
    public Task<int> GetRemoteClientCountAsync(CancellationToken cancellationToken = default)
    {
        return _remoteClientManager.GetClientCountAsync(cancellationToken);
    }

    /// <summary>
    /// 异步获取本地已注册工具数量
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>本地工具数量</returns>
    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetCountAsync(cancellationToken);
    }

    #endregion

    #region Cleanup

    /// <summary>
    /// 异步清除所有本地工具注册
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return _toolRegistry.ClearAsync(cancellationToken);
    }

    /// <summary>
    /// 异步清除所有远程 MCP 客户端
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ClearRemoteClientsAsync(CancellationToken cancellationToken = default)
    {
        await _remoteClientManager.ClearAllClientsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步释放适配器资源（委托给底层工具注册表）
    /// </summary>
    /// <returns>表示异步释放操作的值任务</returns>
    public ValueTask DisposeAsync()
    {
        return _toolRegistry.DisposeAsync();
    }

    #endregion
}

