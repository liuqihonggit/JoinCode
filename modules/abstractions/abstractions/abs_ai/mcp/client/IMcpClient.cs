namespace JoinCode.Abstractions.Mcp.Client;

public interface IMcpClient : IAsyncDisposable {
    /// <summary>获取是否已连接。</summary>
    bool IsConnected { get; }

    /// <summary>获取服务器实现信息。</summary>
    Implementation? ServerInfo { get; }

    /// <summary>获取服务器能力声明。</summary>
    ServerCapabilities? ServerCapabilities { get; }

    event EventHandler<McpNotificationReceivedEventArgs>? NotificationReceived;

    event EventHandler<McpConnectionLostEventArgs>? ConnectionLost;

    /// <summary>设置 elicitation 请求处理器。</summary>
    /// <param name="handler">elicitation 处理器。</param>
    void SetElicitationHandler(IElicitationHandler handler);

    /// <summary>异步连接到 MCP 服务器。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>异步断开连接。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>异步列出服务器可用工具。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<OperationResult<IReadOnlyList<ToolInfo>>> ListToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>异步调用指定工具。</summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="arguments">调用参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="onProgress">进度回调。</param>
    Task<ToolResult> CallToolAsync(
        string toolName,
        Dictionary<string, JsonElement>? arguments = null,
        CancellationToken cancellationToken = default,
        McpProgressCallback? onProgress = null);

    /// <summary>异步列出服务器可用资源。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<OperationResult<IReadOnlyList<McpResource>>> ListResourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>异步读取指定资源内容。</summary>
    /// <param name="uri">资源 URI。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<OperationResult<McpResourceContent?>> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default);

    /// <summary>异步列出服务器可用提示模板。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<OperationResult<IReadOnlyList<McpPrompt>>> ListPromptsAsync(CancellationToken cancellationToken = default);

    /// <summary>异步获取指定提示模板内容。</summary>
    /// <param name="name">提示名称。</param>
    /// <param name="arguments">参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<OperationResult<McpPromptMessage?>> GetPromptAsync(
        string name,
        Dictionary<string, JsonElement>? arguments = null,
        CancellationToken cancellationToken = default);
}

public sealed class McpNotificationReceivedEventArgs : EventArgs {
    /// <summary>获取通知方法名。</summary>
    public required string Method { get; init; }
    /// <summary>获取通知参数。</summary>
    public JsonElement? Params { get; init; }
}

public sealed class McpConnectionLostEventArgs : EventArgs {
    /// <summary>获取服务器名称。</summary>
    public required string ServerName { get; init; }
    /// <summary>获取传输类型。</summary>
    public required string TransportType { get; init; }
    /// <summary>获取连接丢失错误。</summary>
    public Exception? Error { get; init; }
}

public sealed class McpElicitationRequestEventArgs : EventArgs {
    /// <summary>获取服务器名称。</summary>
    public required string ServerName { get; init; }
    /// <summary>获取请求标识。</summary>
    public required JsonRpcId RequestId { get; init; }
    /// <summary>获取 elicitation 请求参数。</summary>
    public required ElicitRequestParams Params { get; init; }
    /// <summary>获取 elicitation 结果。</summary>
    public required ElicitResult Result { get; init; }
}