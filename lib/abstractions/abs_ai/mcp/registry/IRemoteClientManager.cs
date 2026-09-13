namespace JoinCode.Abstractions.Mcp.Registry;

public interface IRemoteClientManager : IAsyncDisposable
{
    event EventHandler<ToolsListChangedEventArgs>? ToolsListChanged;
    event EventHandler<ResourcesListChangedEventArgs>? ResourcesListChanged;
    event EventHandler<PromptsListChangedEventArgs>? PromptsListChanged;

    Task RegisterClientAsync(string clientId, IMcpClient client, CancellationToken cancellationToken = default);

    Task<bool> UnregisterClientAsync(string clientId, CancellationToken cancellationToken = default);

    Task<IMcpClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, IMcpClient>> GetAllClientsAsync(CancellationToken cancellationToken = default);

    Task<RemoteToolsSyncResult> SyncToolsAsync(string clientId, CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<string>>> SyncResourcesAsync(string clientId, CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<string>>> SyncPromptsAsync(string clientId, CancellationToken cancellationToken = default);

    Task<int> GetClientCountAsync(CancellationToken cancellationToken = default);

    Task ClearAllClientsAsync(CancellationToken cancellationToken = default);

    void ClearCache();
}
