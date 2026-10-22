namespace Composition.Tests.Commands;

/// <summary>
/// /mcp 命令单元测试（composition 侧命令类级行为测试）— 覆盖 list(空/已配置 stdio+http 渲染/已连接统计)/
/// add(缺参用法/无效传输/无效作用域/stdio 成功写入条目/http URL 条目)/remove(缺参/未找到/成功)/
/// reconnect 缺参/status 工具清单/enable all/未知操作/配置存储缺失抛 [MCP008]
/// <para>IMcpServerConfigStore 经 DI 注入;IMcpToolRegistry 经 CommandServices.ToolRegistry 字段注入(命令侧优先识别);
/// 不测真实 MCP 连接,仅命令分支与参数校验</para>
/// </summary>
[Collection("command-output")]
public sealed class McpCommandTest {
    private static (McpCommand Command, Mock<IMcpServerConfigStore> Store, ChatCommandContext Context) Arrange(
        string arguments = "",
        Dictionary<string, (string Scope, McpServerConfigEntry Entry)>? servers = null,
        Mock<IMcpToolRegistry>? registry = null) {
        var store = new Mock<IMcpServerConfigStore>();
        store.Setup(s => s.GetAllServersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(servers ?? []);
        store.Setup(s => s.GetConfigPath(It.IsAny<string>())).Returns("/data/.mcp.json");
        var services = new ServiceCollection();
        services.AddSingleton(store.Object);
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
            ToolRegistry = registry?.Object,
        };
        var context = CommandTestHarness.CreateContext(commandServices, arguments: arguments);
        return (new McpCommand(), store, context);
    }

    private static Mock<IMcpToolRegistry> CreateRegistry(
        int localTools = 0, int remoteClients = 0,
        IReadOnlyDictionary<string, IMcpClient>? clients = null) {
        var registry = new Mock<IMcpToolRegistry>();
        registry.Setup(r => r.GetLocalToolCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(localTools);
        registry.Setup(r => r.GetRemoteClientCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(remoteClients);
        registry.Setup(r => r.GetAllRemoteClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(clients ?? new Dictionary<string, IMcpClient>());
        registry.Setup(r => r.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IToolHandler>());
        return registry;
    }

    [Fact]
    public async Task Execute_ListEmptyWithoutRegistry_ShowsEmptyAndRegistryUnavailable() {
        var (cmd, _, context) = Arrange(arguments: "list");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("当前无已配置的 MCP 服务器");
        output.Should().Contain("MCP 工具注册表不可用");
        output.Should().Contain("使用 /mcp add <name> <command|url> 添加服务器");
    }

    [Fact]
    public async Task Execute_ListWithServers_RendersTransportDetails() {
        var servers = new Dictionary<string, (string Scope, McpServerConfigEntry Entry)> {
            ["srv-stdio"] = ("user", new McpServerConfigEntry {
                Type = "stdio", Command = "npx", Args = ["-y", "foo-server"],
            }),
            ["srv-http"] = ("project", new McpServerConfigEntry {
                Type = "http", Url = "https://mcp.example.dev/mcp",
            }),
        };
        var (cmd, _, context) = Arrange(arguments: "list", servers: servers);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已配置的服务器:");
        output.Should().Contain("srv-stdio (用户级)");
        output.Should().Contain("stdio: npx -y foo-server");
        output.Should().Contain("srv-http (项目级)");
        output.Should().Contain("http: https://mcp.example.dev/mcp");
    }

    [Fact]
    public async Task Execute_ListWithRegistry_ShowsConnectedStats() {
        var registry = CreateRegistry(localTools: 5, remoteClients: 0);
        var (cmd, _, context) = Arrange(arguments: "list", registry: registry);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("本地工具: 5 | 远程服务器: 0");
    }

    [Fact]
    public async Task Execute_Status_ShowsToolListAndServerStats() {
        var toolHandler = new Mock<IToolHandler>();
        var tools = new Dictionary<string, IToolHandler> {
            ["tool-a"] = toolHandler.Object,
        };
        var registry = CreateRegistry(localTools: 3, remoteClients: 1);
        registry.Setup(r => r.GetAllToolsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tools);
        var (cmd, _, context) = Arrange(arguments: "status", registry: registry);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== MCP 状态 ===");
        output.Should().Contain("已注册工具:");
        output.Should().Contain("- tool-a");
        output.Should().Contain("总计: 1 个工具");
        output.Should().Contain("本地工具数: 3");
        output.Should().Contain("远程服务器数: 1");
    }

    [Fact]
    public async Task Execute_AddWithoutEnoughArgs_ShowsUsageWithExamples() {
        var (cmd, store, context) = Arrange(arguments: "create myserver");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /mcp add <name> <command|url>");
        output.Should().Contain("示例:");
        store.Verify(s => s.AddServerAsync(It.IsAny<string>(), It.IsAny<McpServerConfigEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_AddWithInvalidTransport_ShowsSupportedTransports() {
        var (cmd, store, context) = Arrange(arguments: "create srv npx foo -t bogus");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("不支持的传输类型: bogus");
        output.Should().Contain("stdio");
        store.Verify(s => s.AddServerAsync(It.IsAny<string>(), It.IsAny<McpServerConfigEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "无效传输不应写入配置");
    }

    [Fact]
    public async Task Execute_AddWithInvalidScope_ShowsSupportedScopes() {
        var (cmd, _, context) = Arrange(arguments: "create srv npx foo -s global");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("不支持的作用域: global");
        output.Should().Contain("user");
    }

    [Fact]
    public async Task Execute_AddStdioServer_PersistsEntryWithCommandAndArgs() {
        var (cmd, store, context) = Arrange(arguments: "create myserver npx -y my-mcp -t stdio -s user -e API_KEY=secret");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已添加 STDIO MCP 服务器 'myserver' 到 user 配置");
        output.Should().Contain("配置文件: /data/.mcp.json");
        output.Should().Contain("使用 /mcp reconnect myserver 连接服务器");
        store.Verify(s => s.AddServerAsync(
            "myserver",
            It.Is<McpServerConfigEntry>(e =>
                e.Type == "stdio" &&
                e.Command == "npx" &&
                e.Args.Contains("-y") && e.Args.Contains("my-mcp") &&
                e.Url == null &&
                e.Env.ContainsKey("API_KEY") && e.Env["API_KEY"] == "secret"),
            "user",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_AddHttpServer_StoresUrlInsteadOfCommand() {
        var (cmd, store, context) = Arrange(arguments: "new sentry https://mcp.sentry.dev/mcp -t http -s project");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("已添加 HTTP MCP 服务器 'sentry' 到 project 配置");
        store.Verify(s => s.AddServerAsync(
            "sentry",
            It.Is<McpServerConfigEntry>(e => e.Type == "http" && e.Url == "https://mcp.sentry.dev/mcp" && e.Command == null),
            "project",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_RemoveWithoutName_ShowsUsage() {
        var (cmd, _, context) = Arrange(arguments: "remove");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /mcp remove <name> [-s user|project]");
    }

    [Fact]
    public async Task Execute_RemoveMissingServer_ShowsNotFound() {
        var (cmd, store, context) = Arrange(arguments: "remove ghost");
        store.Setup(s => s.RemoveServerAsync("ghost", "project", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("在 project 配置中未找到 MCP 服务器 'ghost'");
    }

    [Fact]
    public async Task Execute_RemoveExistingServer_ConfirmsRemoval() {
        var (cmd, store, context) = Arrange(arguments: "rm myserver");
        store.Setup(s => s.RemoveServerAsync("myserver", "project", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已从 project 配置中移除 MCP 服务器 'myserver'");
        store.Verify(s => s.RemoveServerAsync("myserver", "project", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_ReconnectWithoutName_ShowsUsage() {
        var (cmd, _, context) = Arrange(arguments: "reconnect");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("用法: /mcp reconnect <server-name>");
    }

    [Fact]
    public async Task Execute_ReconnectWithoutRegistry_ShowsRegistryUnavailable() {
        var (cmd, _, context) = Arrange(arguments: "reconnect myserver");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("MCP 工具注册表不可用");
    }

    [Fact]
    public async Task Execute_ReconnectConnectedServer_DisconnectsThenConnects() {
        var client = new Mock<IMcpClient>();
        client.Setup(c => c.IsConnected).Returns(true);
        var registry = CreateRegistry();
        registry.Setup(r => r.GetRemoteClientAsync("srv", It.IsAny<CancellationToken>())).ReturnsAsync(client.Object);
        var (cmd, _, context) = Arrange(arguments: "reconnect srv", registry: registry);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已重连 MCP 服务器: srv");
        client.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once, "已连接客户端重连前应先断开");
        client.Verify(c => c.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_EnableAlreadyConnectedClient_ShowsIdempotentHint() {
        var client = new Mock<IMcpClient>();
        client.Setup(c => c.IsConnected).Returns(true);
        var registry = CreateRegistry();
        registry.Setup(r => r.GetRemoteClientAsync("srv", It.IsAny<CancellationToken>())).ReturnsAsync(client.Object);
        var (cmd, _, context) = Arrange(arguments: "enable srv", registry: registry);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("MCP 服务器 srv 已启用", "已连接状态下 enable 应幂等提示而非重复连接");
        client.Verify(c => c.ConnectAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_EnableAllWithoutClients_ShowsEnabledAll() {
        var registry = CreateRegistry(remoteClients: 0);
        var (cmd, _, context) = Arrange(arguments: "enable all", registry: registry);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("已启用所有 MCP 服务器");
        registry.Verify(r => r.GetAllRemoteClientsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// BUG-TASK065-MCP-ADD 已修复（主代理）— CrudAction 枚举补 Add 值（Unix add 习惯等价 create）
    /// + switch 补 case CrudActionEnumConstants.Add：/mcp add 正常进入 AddServerAsync 分支。
    /// </summary>
    [Fact]
    public async Task Execute_AddAction_RoutesToAddServer() {
        var (cmd, store, context) = Arrange(arguments: "add myserver npx my-mcp");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().NotContain("未知操作: add", "add 已被 switch 识别（CrudAction.Add）");
        store.Verify(s => s.AddServerAsync(It.IsAny<string>(), It.IsAny<McpServerConfigEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "add 等价 create/new — 进入 AddServerAsync 分支");
    }

    [Fact]
    public async Task Execute_EnableUnknownServer_ShowsNotFound() {
        var registry = CreateRegistry();
        registry.Setup(r => r.GetRemoteClientAsync("ghost", It.IsAny<CancellationToken>())).ReturnsAsync((IMcpClient?)null);
        var (cmd, _, context) = Arrange(arguments: "enable ghost", registry: registry);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("未找到 MCP 服务器: ghost");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsSupportedActions() {
        var (cmd, _, context) = Arrange(arguments: "bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知操作: bogus");
        output.Should().Contain("可用操作: list, status, add, remove, reconnect, enable, disable");
    }

    [Fact]
    public async Task Execute_WithoutConfigStore_ThrowsMcp008() {
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
        };
        var context = CommandTestHarness.CreateContext(commandServices, arguments: "list");

        var act = () => CommandTestHarness.CaptureOutputAsync(() => new McpCommand().ExecuteAsync(context));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*[MCP008]*");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new McpCommand();
        cmd.Name.Should().Be("mcp");
        cmd.Usage.Should().StartWith("/mcp");
        cmd.Aliases.Should().BeEmpty();
    }
}
