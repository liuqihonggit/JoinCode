namespace Mcp.Tests;

/// <summary>
/// McpClientToolHandlers.McpConnectAsync 编排逻辑确定性测试 —
/// 验证参数校验→锁→auth分派→connect→注册→save 主流程,用 fake client/factory 消除 IO。
/// 不传 fileSystem 使 SaveStateAsync 成为空操作,无磁盘 IO/无时序。
/// http/websocket transport 走 ClientFactory 分支,避免 stdio 启动子进程的 IO。
/// oauth 子分支依赖 sealed McpOAuthService(StartAuthorizationFlowAsync 启动 HttpListener + PKCE HTTP 流程),
/// 属不可 mock 时序依赖,需集成测试覆盖,此处跳过。
/// </summary>
public sealed class McpClientToolHandlersConnectTests {

    [Fact]
    public async Task McpConnectAsync_EmptyConnectionName_ReturnsError() {
        await using var handler = new McpClientToolHandlers();
        var result = await handler.McpConnectAsync("", "http://x", "http");
        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain(L.T(StringKey.ConnectionNameCannotBeEmpty));
    }

    [Fact]
    public async Task McpConnectAsync_WhitespaceConnectionName_ReturnsError() {
        await using var handler = new McpClientToolHandlers();
        var result = await handler.McpConnectAsync("   ", "http://x", "http");
        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain(L.T(StringKey.ConnectionNameCannotBeEmpty));
    }

    [Fact]
    public async Task McpConnectAsync_EmptyEndpoint_ReturnsError() {
        await using var handler = new McpClientToolHandlers();
        var result = await handler.McpConnectAsync("conn", "", "http");
        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain(L.T(StringKey.EndpointCannotBeEmpty));
    }

    [Fact]
    public async Task McpConnectAsync_WhitespaceEndpoint_ReturnsError() {
        await using var handler = new McpClientToolHandlers();
        var result = await handler.McpConnectAsync("conn", "   ", "http");
        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain(L.T(StringKey.EndpointCannotBeEmpty));
    }

    [Fact]
    public async Task McpConnectAsync_WithFakeFactory_ReturnsSuccessWithServerInfo() {
        var fakeClient = new ConnectFakeMcpClient();
        var factory = new ConnectFakeClientFactory(fakeClient);
        var deps = new McpClientToolDeps(ClientFactory: factory);
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        var result = await handler.McpConnectAsync("conn", "http://localhost:8080", "http");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain(L.T(StringKey.ConnectedToMcpServer, "conn"));
        result.GetFirstText().Should().Contain(L.T(StringKey.LabelServer, "FakeServer"));
    }

    [Fact]
    public async Task McpConnectAsync_WebSocketTransport_WithFakeFactory_Succeeds() {
        var fakeClient = new ConnectFakeMcpClient();
        var factory = new ConnectFakeClientFactory(fakeClient);
        var deps = new McpClientToolDeps(ClientFactory: factory);
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        var result = await handler.McpConnectAsync("wsconn", "ws://localhost:8080", "websocket");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain(L.T(StringKey.ConnectedToMcpServer, "wsconn"));
    }

    [Fact]
    public async Task McpConnectAsync_DuplicateConnectionName_ReturnsError() {
        var fakeClient = new ConnectFakeMcpClient();
        var factory = new ConnectFakeClientFactory(fakeClient);
        var deps = new McpClientToolDeps(ClientFactory: factory);
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        var first = await handler.McpConnectAsync("dup", "http://x", "http");
        first.IsError.Should().BeFalse();

        var second = await handler.McpConnectAsync("dup", "http://y", "http");
        second.IsError.Should().BeTrue();
        second.GetFirstText().Should().Contain(L.T(StringKey.ConnectionAlreadyExists, "dup"));
    }

    [Fact]
    public async Task McpConnectAsync_ThenDisconnect_Succeeds() {
        var fakeClient = new ConnectFakeMcpClient();
        var factory = new ConnectFakeClientFactory(fakeClient);
        var deps = new McpClientToolDeps(ClientFactory: factory);
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        await handler.McpConnectAsync("conn", "http://x", "http");
        var result = await handler.McpDisconnectAsync("conn");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain(L.T(StringKey.Disconnected, "conn"));
    }

    [Fact]
    public async Task McpConnectAsync_AuthNameWithoutAuthHandler_StillSucceeds() {
        var fakeClient = new ConnectFakeMcpClient();
        var factory = new ConnectFakeClientFactory(fakeClient);
        var deps = new McpClientToolDeps(ClientFactory: factory);
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        var result = await handler.McpConnectAsync("conn", "http://x", "http", auth_name: "myauth");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain(L.T(StringKey.ConnectedToMcpServer, "conn"));
    }

    [Fact]
    public async Task McpConnectAsync_DisabledServer_ReturnsError() {
        var fs = new InMemoryFileSystem();
        var manager = new McpServerStateManager(fs, "mem/mcp_disabled.json");
        await manager.DisableAsync("conn");

        var fakeClient = new ConnectFakeMcpClient();
        var factory = new ConnectFakeClientFactory(fakeClient);
        var deps = new McpClientToolDeps(ServerStateManager: manager, ClientFactory: factory);
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        var result = await handler.McpConnectAsync("conn", "http://x", "http");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("已被禁用");
    }

    [Fact]
    public async Task McpConnectAsync_WithToolRegistry_SyncsRemoteTools() {
        var fakeClient = new ConnectFakeMcpClient();
        var factory = new ConnectFakeClientFactory(fakeClient);
        var registry = new ConnectFakeToolRegistry();
        var deps = new McpClientToolDeps(ClientFactory: factory, ToolRegistry: registry);
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        var result = await handler.McpConnectAsync("conn", "http://x", "http");

        result.IsError.Should().BeFalse();
        registry.SyncedClients.Should().Contain("conn");
    }

    [Fact]
    public async Task McpConnectAsync_WithElicitationHandler_Succeeds() {
        var fakeClient = new ConnectFakeMcpClient();
        var factory = new ConnectFakeClientFactory(fakeClient);
        var deps = new McpClientToolDeps(ClientFactory: factory, ElicitationHandler: new ConnectFakeElicitationHandler());
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        var result = await handler.McpConnectAsync("conn", "http://x", "http");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain(L.T(StringKey.ConnectedToMcpServer, "conn"));
    }

    [Fact]
    public async Task McpConnectAsync_ConnectThrows_ReturnsErrorResult() {
        var fakeClient = new ConnectThrowsFakeMcpClient();
        var factory = new ConnectFakeClientFactory(fakeClient);
        var deps = new McpClientToolDeps(ClientFactory: factory);
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        var result = await handler.McpConnectAsync("conn", "http://x", "http");

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task McpConnectAsync_AuthNameFound_UsesAuthConfigAndSucceeds() {
        await using var authHandler = new McpAuthToolHandlers();
        await authHandler.McpAuthApiKeyAsync("myauth", "secret-key", "X-API-Key");

        var fakeClient = new ConnectFakeMcpClient();
        var factory = new ConnectFakeClientFactory(fakeClient);
        var deps = new McpClientToolDeps(AuthToolHandlers: authHandler, ClientFactory: factory);
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        var result = await handler.McpConnectAsync("conn", "http://x", "http", auth_name: "myauth");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain(L.T(StringKey.ConnectedToMcpServer, "conn"));
    }

    [Fact]
    public async Task McpConnectAsync_AuthNameNotFound_ReturnsError() {
        await using var authHandler = new McpAuthToolHandlers();

        var fakeClient = new ConnectFakeMcpClient();
        var factory = new ConnectFakeClientFactory(fakeClient);
        var deps = new McpClientToolDeps(AuthToolHandlers: authHandler, ClientFactory: factory);
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);

        var result = await handler.McpConnectAsync("conn", "http://x", "http", auth_name: "nonexistent");

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain(L.T(StringKey.AuthConfigNotFound, "nonexistent"));
    }

    private sealed class ConnectFakeMcpClient : IMcpClient {
        public bool IsConnected => true;
        public Implementation? ServerInfo => new() { Name = "FakeServer", Version = "1.0" };
        public ServerCapabilities? ServerCapabilities => new() { Tools = new ToolsCapability() };

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

    private sealed class ConnectFakeClientFactory : IMcpClientFactory {
        private readonly IMcpClient _client;
        public ConnectFakeClientFactory(IMcpClient client) => _client = client;
        public IMcpClient CreateClient(McpServerConnectionConfig config, ILogger? logger = null) => _client;
        public IMcpClient CreateClient(McpServerConnectionConfig config, bool enableFallback, ILogger? logger = null) => _client;
    }

    private sealed class ConnectThrowsFakeMcpClient : IMcpClient {
        public bool IsConnected => false;
        public Implementation? ServerInfo => new() { Name = "Throw", Version = "0" };
        public ServerCapabilities? ServerCapabilities => null;

        public event EventHandler<McpNotificationReceivedEventArgs>? NotificationReceived = (_, _) => { };
        public event EventHandler<McpConnectionLostEventArgs>? ConnectionLost = (_, _) => { };

        public void SetElicitationHandler(IElicitationHandler handler) { }
        public Task ConnectAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("connect boom");
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

    private sealed class ConnectFakeToolRegistry : IMcpToolRegistry {
        public List<string> SyncedClients { get; } = [];
        public Task<RemoteToolsSyncResult> SyncRemoteToolsAsync(string clientId, CancellationToken cancellationToken = default) {
            SyncedClients.Add(clientId);
            return Task.FromResult(new RemoteToolsSyncResult(true, []));
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

    private sealed class ConnectFakeElicitationHandler : IElicitationHandler {
        public Task<ElicitResult> HandleElicitationAsync(string serverName, JsonRpcId requestId, ElicitRequestParams @params, CancellationToken cancellationToken)
            => Task.FromResult(new ElicitResult());
    }
}
