namespace Mcp.Tests;

/// <summary>
/// McpConnectAsync 拆分出的 internal 子方法确定性测试 —
/// BuildBaseConnectionConfig / CreateClientByTransport / BuildConnectResponse (纯计算,无 IO/无时序)
/// </summary>
public sealed class McpClientToolHandlersConnectSubMethodTests {

    [Fact]
    public void BuildBaseConnectionConfig_PlainEndpoint_NoExpansion() {
        var config = McpClientToolHandlers.BuildBaseConnectionConfig("conn", "http://localhost:8080", "http");
        config.Name.Should().Be("conn");
        config.Endpoint.Should().Be("http://localhost:8080");
        config.TransportType.Should().Be(McpClientTransportType.Http);
    }

    [Fact]
    public void BuildBaseConnectionConfig_EnvVarEndpoint_ExpandsDefault() {
        var config = McpClientToolHandlers.BuildBaseConnectionConfig("c", "${MCP_TEST_UNDEFINED_VAR_98765:-/fallback}/bin", "stdio");
        config.Endpoint.Should().Be("/fallback/bin");
        config.TransportType.Should().Be(McpClientTransportType.Stdio);
    }

    [Theory]
    [InlineData("stdio", McpClientTransportType.Stdio)]
    [InlineData("http", McpClientTransportType.Http)]
    [InlineData("websocket", McpClientTransportType.WebSocket)]
    public void BuildBaseConnectionConfig_VariousTransports_Parses(string transport, McpClientTransportType expected) {
        var config = McpClientToolHandlers.BuildBaseConnectionConfig("c", "ep", transport);
        config.TransportType.Should().Be(expected);
    }

    [Fact]
    public void BuildBaseConnectionConfig_InvalidTransport_Throws() {
        var act = () => McpClientToolHandlers.BuildBaseConnectionConfig("c", "ep", "bogus");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task CreateClientByTransport_Stdio_ReturnsStdioClient() {
        await using var handler = new McpClientToolHandlers();
        var config = new McpServerConnectionConfig { Name = "c", Endpoint = "ep", TransportType = McpClientTransportType.Stdio };
        await using var client = handler.CreateClientByTransport(config, "stdio");
        client.Should().BeOfType<McpStdioClient>();
    }

    [Fact]
    public async Task CreateClientByTransport_HttpNoFactory_ReturnsHttpClient() {
        await using var handler = new McpClientToolHandlers();
        var config = new McpServerConnectionConfig { Name = "c", Endpoint = "http://x", TransportType = McpClientTransportType.Http };
        await using var client = handler.CreateClientByTransport(config, "http");
        client.Should().BeOfType<McpHttpClient>();
    }

    [Fact]
    public async Task CreateClientByTransport_WebSocketNoFactory_ReturnsWebSocketClient() {
        await using var handler = new McpClientToolHandlers();
        var config = new McpServerConnectionConfig { Name = "c", Endpoint = "ws://x", TransportType = McpClientTransportType.WebSocket };
        await using var client = handler.CreateClientByTransport(config, "websocket");
        client.Should().BeOfType<McpWebSocketClient>();
    }

    [Fact]
    public async Task CreateClientByTransport_WithFactory_UsesFactory() {
        var fakeClient = new ConfigurableFakeMcpClient();
        var factory = new SimpleFakeClientFactory(fakeClient);
        var deps = new McpClientToolDeps(ClientFactory: factory);
        await using var handler = new McpClientToolHandlers(deps, NullLogger<McpClientToolHandlers>.Instance);
        var config = new McpServerConnectionConfig { Name = "c", Endpoint = "http://x", TransportType = McpClientTransportType.Http };
        await using var client = handler.CreateClientByTransport(config, "http");
        client.Should().BeSameAs(fakeClient);
    }

    [Fact]
    public async Task BuildConnectResponse_FullServerInfoAndCaps_ContainsAllInfo() {
        await using var client = new ConfigurableFakeMcpClient {
            ServerInfoOverride = new Implementation { Name = "MyServer", Version = "2.0" },
            CapabilitiesOverride = new ServerCapabilities {
                Tools = new ToolsCapability(),
                Resources = new ResourcesCapability(),
                Prompts = new PromptsCapability()
            }
        };
        var text = McpClientToolHandlers.BuildConnectResponse("conn", client);
        text.Should().Contain("ConnectedToMcpServer");
        text.Should().Contain("SupportsTools");
        text.Should().Contain("SupportsResources");
        text.Should().Contain("SupportsPrompts");
    }

    [Fact]
    public async Task BuildConnectResponse_NullServerInfo_DoesNotCrash() {
        await using var client = new ConfigurableFakeMcpClient { ServerInfoOverride = null };
        var text = McpClientToolHandlers.BuildConnectResponse("c", client);
        text.Should().Contain("LabelServer");
        text.Should().Contain("LabelVersion");
    }

    [Fact]
    public async Task BuildConnectResponse_NullCapabilities_OmitsCapabilityLines() {
        await using var client = new ConfigurableFakeMcpClient { CapabilitiesOverride = null };
        var text = McpClientToolHandlers.BuildConnectResponse("c", client);
        text.Should().NotContain("SupportsTools");
        text.Should().NotContain("SupportsResources");
        text.Should().NotContain("SupportsPrompts");
    }

    [Fact]
    public async Task BuildConnectResponse_OnlyToolsCapability_IncludesToolsLineOnly() {
        await using var client = new ConfigurableFakeMcpClient {
            ServerInfoOverride = new Implementation { Name = "S", Version = "1" },
            CapabilitiesOverride = new ServerCapabilities { Tools = new ToolsCapability() }
        };
        var text = McpClientToolHandlers.BuildConnectResponse("c", client);
        text.Should().Contain("SupportsTools");
        text.Should().NotContain("SupportsResources");
        text.Should().NotContain("SupportsPrompts");
    }

    private sealed class ConfigurableFakeMcpClient : IMcpClient {
        public Implementation? ServerInfoOverride { get; set; } = new() { Name = "Fake", Version = "1.0" };
        public ServerCapabilities? CapabilitiesOverride { get; set; }
        public bool IsConnected => true;
        public Implementation? ServerInfo => ServerInfoOverride;
        public ServerCapabilities? ServerCapabilities => CapabilitiesOverride;

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

    private sealed class SimpleFakeClientFactory : IMcpClientFactory {
        private readonly IMcpClient _client;
        public SimpleFakeClientFactory(IMcpClient client) => _client = client;
        public IMcpClient CreateClient(McpServerConnectionConfig config, ILogger? logger = null) => _client;
        public IMcpClient CreateClient(McpServerConnectionConfig config, bool enableFallback, ILogger? logger = null) => _client;
    }
}
