namespace JoinCode.Abstractions.Mcp.Registry;

public interface IRemoteClientManager : IAsyncDisposable {
    event EventHandler<ToolsListChangedEventArgs>? ToolsListChanged;
    event EventHandler<ResourcesListChangedEventArgs>? ResourcesListChanged;
    event EventHandler<PromptsListChangedEventArgs>? PromptsListChanged;

    /// <summary>注册远程客户端。</summary>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="client">MCP 客户端。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RegisterClientAsync(string clientId, IMcpClient client, CancellationToken cancellationToken = default);

    /// <summary>注销远程客户端。</summary>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> UnregisterClientAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>获取指定客户端。</summary>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IMcpClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>获取所有客户端。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyDictionary<string, IMcpClient>> GetAllClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>同步指定客户端的工具列表。</summary>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<RemoteToolsSyncResult> SyncToolsAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>同步指定客户端的资源列表。</summary>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<OperationResult<IReadOnlyList<string>>> SyncResourcesAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>同步指定客户端的提示列表。</summary>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<OperationResult<IReadOnlyList<string>>> SyncPromptsAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>获取客户端数量。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<int> GetClientCountAsync(CancellationToken cancellationToken = default);

    /// <summary>清空所有客户端。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ClearAllClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>清空缓存。</summary>
    void ClearCache();
}
