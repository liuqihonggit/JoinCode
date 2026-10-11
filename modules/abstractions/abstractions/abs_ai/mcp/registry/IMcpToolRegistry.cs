namespace JoinCode.Abstractions.Mcp.Registry;

public interface IMcpToolRegistry : IToolRegistry, IRegistry {
    /// <summary>注册远程 MCP 客户端。</summary>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="client">MCP 客户端实例。</param>
    void RegisterRemoteClient(string clientId, IMcpClient client);

    /// <summary>异步注销远程 MCP 客户端。</summary>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> UnregisterRemoteClientAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>异步获取远程 MCP 客户端。</summary>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IMcpClient?> GetRemoteClientAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>异步获取全部远程 MCP 客户端。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyDictionary<string, IMcpClient>> GetAllRemoteClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>异步同步远程工具列表。</summary>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<RemoteToolsSyncResult> SyncRemoteToolsAsync(
        string clientId,
        CancellationToken cancellationToken = default);

    /// <summary>清除缓存。</summary>
    void ClearCache();

    /// <summary>异步获取本地工具数量。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<int> GetLocalToolCountAsync(CancellationToken cancellationToken = default);

    /// <summary>异步获取远程客户端数量。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<int> GetRemoteClientCountAsync(CancellationToken cancellationToken = default);

    /// <summary>异步清除全部远程客户端。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ClearRemoteClientsAsync(CancellationToken cancellationToken = default);
}

public sealed record RemoteToolsSyncResult(
    bool Success,
    IReadOnlyList<string> ToolNames,
    string? ErrorMessage = null,
    ToolDriftReport? DriftReport = null,
    McpReconnectResult? ReconnectResult = null);

public sealed class ToolsListChangedEventArgs : EventArgs {
    /// <summary>获取客户端标识。</summary>
    public required string ClientId { get; init; }
    /// <summary>获取同步结果。</summary>
    public required RemoteToolsSyncResult SyncResult { get; init; }
}

public sealed class ResourcesListChangedEventArgs : EventArgs {
    /// <summary>获取客户端标识。</summary>
    public required string ClientId { get; init; }
    /// <summary>获取同步结果。</summary>
    public required OperationResult<IReadOnlyList<string>> SyncResult { get; init; }
}

public sealed class PromptsListChangedEventArgs : EventArgs {
    /// <summary>获取客户端标识。</summary>
    public required string ClientId { get; init; }
    /// <summary>获取同步结果。</summary>
    public required OperationResult<IReadOnlyList<string>> SyncResult { get; init; }
}