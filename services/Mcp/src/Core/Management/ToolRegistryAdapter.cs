using JoinCode.Abstractions.Attributes;

namespace McpToolRegistry;

[Register]
public sealed partial class ToolRegistryAdapter : IMcpToolRegistry
{
    private readonly IToolRegistry _toolRegistry;
    private readonly RemoteClientManager _remoteClientManager;
    [Inject] private readonly ILogger<ToolRegistryAdapter>? _logger;

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

    public async Task RegisterToolAsync(IToolHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        await _toolRegistry.RegisterToolAsync(handler, cancellationToken);

        _logger?.LogDebug("MCP tool registered via adapter: {ToolName}", handler.Name);
    }

    public async Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(handler);

        var delegateHandler = new DelegateToolHandler(name, description, inputSchema, handler);
        await _toolRegistry.RegisterToolAsync(delegateHandler, cancellationToken);
    }

    public Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        return _toolRegistry.UnregisterToolAsync(toolName, cancellationToken);
    }

    #endregion

    #region Remote MCP Client Management

    public void RegisterRemoteClient(string clientId, IMcpClient client)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentNullException.ThrowIfNull(client);

        _ = _remoteClientManager.RegisterClientAsync(clientId, client, CancellationToken.None).ConfigureAwait(false);
    }

    public Task<bool> UnregisterRemoteClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        return _remoteClientManager.UnregisterClientAsync(clientId, cancellationToken);
    }

    public Task<IMcpClient?> GetRemoteClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        return _remoteClientManager.GetClientAsync(clientId, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, IMcpClient>> GetAllRemoteClientsAsync(CancellationToken cancellationToken = default)
    {
        return _remoteClientManager.GetAllClientsAsync(cancellationToken);
    }

    public Task<RemoteToolsSyncResult> SyncRemoteToolsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        return _remoteClientManager.SyncToolsAsync(clientId, cancellationToken);
    }

    #endregion

    #region Tool Execution

    public Task<IToolHandler?> GetToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetToolAsync(toolName, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, IToolHandler>> GetAllToolsAsync(CancellationToken cancellationToken = default)
    {
        return await _toolRegistry.GetAllToolsAsync(cancellationToken);
    }

    public Task<ToolResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        return _toolRegistry.ExecuteToolAsync(toolName, arguments, cancellationToken, onProgress);
    }

    public Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetToolInfoAsync(toolName, cancellationToken);
    }

    public Task<IReadOnlyList<ToolInfo>> GetAllToolInfosAsync(CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetAllToolInfosAsync(cancellationToken);
    }

    public Task<bool> ContainsToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        return _toolRegistry.ContainsToolAsync(toolName, cancellationToken);
    }

    #endregion

    #region Cache Management

    public void ClearCache()
    {
        _remoteClientManager.ClearCache();
    }

    #endregion

    #region Statistics

    public Task<int> GetLocalToolCountAsync(CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetCountAsync(cancellationToken);
    }

    public Task<int> GetRemoteClientCountAsync(CancellationToken cancellationToken = default)
    {
        return _remoteClientManager.GetClientCountAsync(cancellationToken);
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return _toolRegistry.GetCountAsync(cancellationToken);
    }

    #endregion

    #region Cleanup

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return _toolRegistry.ClearAsync(cancellationToken);
    }

    public async Task ClearRemoteClientsAsync(CancellationToken cancellationToken = default)
    {
        await _remoteClientManager.ClearAllClientsAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return _toolRegistry.DisposeAsync();
    }

    #endregion
}

