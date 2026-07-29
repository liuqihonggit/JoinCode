namespace JoinCode.Abstractions.Mcp.Registry;

public interface IMcpToolRegistry : IToolRegistry
{
    void RegisterRemoteClient(string clientId, IMcpClient client);

    Task<bool> UnregisterRemoteClientAsync(string clientId, CancellationToken cancellationToken = default);

    Task<IMcpClient?> GetRemoteClientAsync(string clientId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, IMcpClient>> GetAllRemoteClientsAsync(CancellationToken cancellationToken = default);

    Task<RemoteToolsSyncResult> SyncRemoteToolsAsync(
        string clientId,
        CancellationToken cancellationToken = default);

    void ClearCache();

    Task<int> GetLocalToolCountAsync(CancellationToken cancellationToken = default);

    Task<int> GetRemoteClientCountAsync(CancellationToken cancellationToken = default);

    Task ClearRemoteClientsAsync(CancellationToken cancellationToken = default);
}

public sealed record RemoteToolsSyncResult(
    bool Success,
    IReadOnlyList<string> ToolNames,
    string? ErrorMessage = null,
    ToolDriftReport? DriftReport = null,
    McpReconnectResult? ReconnectResult = null);

public sealed class ToolsListChangedEventArgs : EventArgs
{
    public required string ClientId { get; init; }
    public required RemoteToolsSyncResult SyncResult { get; init; }
}

public sealed class ResourcesListChangedEventArgs : EventArgs
{
    public required string ClientId { get; init; }
    public required OperationResult<IReadOnlyList<string>> SyncResult { get; init; }
}

public sealed class PromptsListChangedEventArgs : EventArgs
{
    public required string ClientId { get; init; }
    public required OperationResult<IReadOnlyList<string>> SyncResult { get; init; }
}
