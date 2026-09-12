namespace JoinCode.Abstractions.Mcp.Client;

public interface IMcpClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Implementation? ServerInfo { get; }

    ServerCapabilities? ServerCapabilities { get; }

    event EventHandler<McpNotificationReceivedEventArgs>? NotificationReceived;

    event EventHandler<McpConnectionLostEventArgs>? ConnectionLost;

    void SetElicitationHandler(IElicitationHandler handler);

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<ToolInfo>>> ListToolsAsync(CancellationToken cancellationToken = default);

    Task<ToolResult> CallToolAsync(
        string toolName,
        Dictionary<string, JsonElement>? arguments = null,
        CancellationToken cancellationToken = default,
        McpProgressCallback? onProgress = null);

    Task<OperationResult<IReadOnlyList<McpResource>>> ListResourcesAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<McpResourceContent?>> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<McpPrompt>>> ListPromptsAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<McpPromptMessage?>> GetPromptAsync(
        string name,
        Dictionary<string, JsonElement>? arguments = null,
        CancellationToken cancellationToken = default);
}

public sealed class McpNotificationReceivedEventArgs : EventArgs
{
    public required string Method { get; init; }
    public JsonElement? Params { get; init; }
}

public sealed class McpConnectionLostEventArgs : EventArgs
{
    public required string ServerName { get; init; }
    public required string TransportType { get; init; }
    public Exception? Error { get; init; }
}

public sealed class McpElicitationRequestEventArgs : EventArgs
{
    public required string ServerName { get; init; }
    public required JsonRpcId RequestId { get; init; }
    public required ElicitRequestParams Params { get; init; }
    public required ElicitResult Result { get; init; }
}
