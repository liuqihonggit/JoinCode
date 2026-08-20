namespace Mcp.Tests;

/// <summary>
/// McpConnectAsync 连接成功后应同步远程工具到注册表，
/// 否则 ToolSearch 无法发现刚连接的 MCP 工具（两阶段加载链路断裂）。
/// </summary>
public sealed class McpClientToolHandlersToolSyncTests
{
    [Fact]
    public async Task McpConnectAsync_ConnectionSucceeded_SyncsRemoteTools()
    {
        var client = new FakeMcpClient();
        var factory = new FakeClientFactory(client);
        var registry = new FakeMcpToolRegistry();

        var deps = new McpClientToolDeps(
            ToolRegistry: registry,
            ClientFactory: factory);

        var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        var result = await handler.McpConnectAsync("mock", "http://localhost:18090/mcp", "http", cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        registry.SyncedClients.Should().Contain("mock", "连接成功后应同步远程工具，使 ToolSearch 可发现");
    }

    private sealed class FakeMcpClient : IMcpClient
    {
        public bool IsConnected => true;
        public Implementation? ServerInfo => new Implementation { Name = "FakeMcp", Version = "1.0.0" };
        public ServerCapabilities? ServerCapabilities => new ServerCapabilities { Tools = new ToolsCapability { ListChanged = true } };

        public event EventHandler<McpNotificationReceivedEventArgs>? NotificationReceived = (_, _) => { };
        public event EventHandler<McpConnectionLostEventArgs>? ConnectionLost = (_, _) => { };

        public void SetElicitationHandler(IElicitationHandler handler) { }
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<OperationResult<IReadOnlyList<ToolInfo>>> ListToolsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IReadOnlyList<ToolInfo>>.Ok([]));

        public Task<ToolResult> CallToolAsync(string toolName, Dictionary<string, JsonElement>? arguments = null, CancellationToken cancellationToken = default, McpProgressCallback? onProgress = null)
            => Task.FromResult(ToolResultBuilder.Success().WithText("ok").Build());

        public Task<OperationResult<IReadOnlyList<McpResource>>> ListResourcesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IReadOnlyList<McpResource>>.Ok([]));

        public Task<OperationResult<McpResourceContent?>> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<McpResourceContent?>.Ok(null));

        public Task<OperationResult<IReadOnlyList<McpPrompt>>> ListPromptsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IReadOnlyList<McpPrompt>>.Ok([]));

        public Task<OperationResult<McpPromptMessage?>> GetPromptAsync(string name, Dictionary<string, JsonElement>? arguments = null, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<McpPromptMessage?>.Ok(null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeClientFactory : IMcpClientFactory
    {
        private readonly IMcpClient _client;
        public FakeClientFactory(IMcpClient client) => _client = client;

        public IMcpClient CreateClient(McpServerConnectionConfig config, ILogger? logger = null) => _client;
        public IMcpClient CreateClient(McpServerConnectionConfig config, bool enableFallback, ILogger? logger = null) => _client;
    }

    private sealed class FakeMcpToolRegistry : IMcpToolRegistry
    {
        public List<string> SyncedClients { get; } = [];

        public Task<RemoteToolsSyncResult> SyncRemoteToolsAsync(string clientId, CancellationToken cancellationToken = default)
        {
            SyncedClients.Add(clientId);
            return Task.FromResult(new RemoteToolsSyncResult(true, ["mock_echo"]));
        }

        public void RegisterRemoteClient(string clientId, IMcpClient client) { }
        public Task<bool> UnregisterRemoteClientAsync(string clientId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IMcpClient?> GetRemoteClientAsync(string clientId, CancellationToken cancellationToken = default) => Task.FromResult<IMcpClient?>(null);
        public Task<IReadOnlyDictionary<string, IMcpClient>> GetAllRemoteClientsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, IMcpClient>>(new Dictionary<string, IMcpClient>());
        public void ClearCache() { }
        public Task<int> GetLocalToolCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetRemoteClientCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task ClearRemoteClientsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RegisterToolAsync(IToolHandler handler, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default, ToolKind kind = ToolKind.System, string? groupName = null, ToolTimeoutPolicy? timeoutPolicy = null, string? category = null) => Task.CompletedTask;
        public Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IToolHandler?> GetToolAsync(string toolName, CancellationToken cancellationToken = default) => Task.FromResult<IToolHandler?>(null);
        public Task<IReadOnlyDictionary<string, IToolHandler>> GetAllToolsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, IToolHandler>>(new Dictionary<string, IToolHandler>());
        public Task<ToolResult> ExecuteToolAsync(string toolName, Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default, ToolProgressCallback? onProgress = null) => Task.FromResult(ToolResultBuilder.Success().WithText("ok").Build());
        public Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default) => Task.FromResult<ToolInfo?>(null);
        public Task<IReadOnlyList<ToolInfo>> GetAllToolInfosAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ToolInfo>>([]);
        public Task<bool> ContainsToolAsync(string toolName, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<int> GetCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<FrozenSet<string>> GetGroupNamesAsync(CancellationToken cancellationToken = default) => Task.FromResult(FrozenSet.Create<string>());
        public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByKindAsync(ToolKind kind, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, IToolHandler>>(new Dictionary<string, IToolHandler>());
        public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByGroupAsync(string groupName, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, IToolHandler>>(new Dictionary<string, IToolHandler>());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}