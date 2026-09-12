namespace Mcp.Tests;

/// <summary>
/// McpClientToolHandlers 持久化测试 — 验证 SaveStateAsync/LoadState 跨进程共享连接配置。
/// </summary>
public sealed class McpClientToolHandlersPersistenceTests
{
    private static (McpClientToolHandlers handler, InMemoryFileSystem fs) CreateHandlerWithFileSystem()
    {
        var fs = new InMemoryFileSystem();
        var client = new FakeMcpClient();
        var factory = new FakeClientFactory(client);
        var registry = new FakeMcpToolRegistry();
        var deps = new McpClientToolDeps(ToolRegistry: registry, ClientFactory: factory);
        var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance, fs);
        return (handler, fs);
    }

    private static string GetConnectionsFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            JoinCode.Abstractions.Configuration.AppData.AppDataConstants.AppDataFolder,
            JoinCode.Abstractions.Configuration.AppData.AppDataConstants.McpFolderName,
            JoinCode.Abstractions.Configuration.AppData.AppDataConstants.McpConnectionsFileName);
    }

    [Fact]
    public async Task McpConnectAsync_WithFileSystem_PersistsConnections()
    {
        var (handler, fs) = CreateHandlerWithFileSystem();
        var filePath = GetConnectionsFilePath();

        var result = await handler.McpConnectAsync("testconn", "http://localhost:18090/mcp", "http", cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse("连接应成功");
        fs.FileExists(filePath).Should().BeTrue("连接成功后应持久化到 connections.json");

        var json = fs.ReadAllText(filePath);
        json.Should().Contain("testconn", "持久化内容应包含连接名");
        json.Should().Contain("http://localhost:18090/mcp", "持久化内容应包含 endpoint");
        json.Should().Contain("http", "持久化内容应包含 transport_type");

        await handler.DisposeAsync();
    }

    [Fact]
    public async Task McpDisconnectAsync_WithFileSystem_RemovesConnection()
    {
        var (handler, fs) = CreateHandlerWithFileSystem();
        var filePath = GetConnectionsFilePath();

        await handler.McpConnectAsync("testconn", "http://localhost:18090/mcp", "http", cancellationToken: CancellationToken.None);
        fs.FileExists(filePath).Should().BeTrue("连接后应存在 connections.json");

        var result = await handler.McpDisconnectAsync("testconn", CancellationToken.None);

        result.IsError.Should().BeFalse("断开应成功");
        var json = fs.ReadAllText(filePath);
        json.Should().NotContain("testconn", "断开后 connections.json 应移除该连接");

        await handler.DisposeAsync();
    }

    [Fact]
    public async Task Constructor_WithFileSystem_RestoresPersistedConnections()
    {
        var fs = new InMemoryFileSystem();
        var filePath = GetConnectionsFilePath();
        var dir = Path.GetDirectoryName(filePath)!;
        fs.CreateDirectory(dir);
        fs.WriteAllText(filePath, """{"connections":[{"name":"restored","endpoint":"http://localhost:18090/mcp","transportType":"http","useOAuth":false,"authName":null}]}""");

        var client = new FakeMcpClient();
        var factory = new FakeClientFactory(client);
        var registry = new FakeMcpToolRegistry();
        var deps = new McpClientToolDeps(ToolRegistry: registry, ClientFactory: factory);

        var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance, fs);

        await Task.Delay(500);
        var listResult = await handler.McpListToolsAsync("restored", CancellationToken.None);
        listResult.IsError.Should().BeFalse("恢复的连接应可列出工具");

        await handler.DisposeAsync();
    }

    private sealed class FakeMcpClient : IMcpClient
    {
        public bool IsConnected => true;
        public Implementation? ServerInfo => new() { Name = "FakeMcp", Version = "1.0.0" };
        public ServerCapabilities? ServerCapabilities => new() { Tools = new ToolsCapability { ListChanged = true } };

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
