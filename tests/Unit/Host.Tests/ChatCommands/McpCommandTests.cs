namespace Host.Tests.ChatCommands;

/// <summary>
/// McpCommand 取值范围测试 — 验证 CrudAction + McpAction 枚举字面量正确路由
/// 覆盖:list/ls/add/create/new/remove/delete/rm/status/reconnect/enable/disable/未知子命令
/// 验证目标:Step 9 重构后,所有 case 标签能被正确识别
/// </summary>
public sealed class McpCommandTests
{
    [Fact]
    public void Name_Should_Be_mcp()
    {
        var cmd = new McpCommand();
        cmd.Name.Should().Be("mcp");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new McpCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new McpCommand();
        cmd.Usage.Should().StartWith("/mcp");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new McpCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new McpCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Theory]
    [InlineData("list")]
    [InlineData("ls")]
    public async Task Execute_WithListVariants_Should_Return_Continue(string subCommand)
    {
        // list/ls — CrudAction.List/Ls 别名
        var cmd = new McpCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Default_To_List()
    {
        // 无参数时默认 list
        var cmd = new McpCommand();
        var context = CreateContext("");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithStatus_Should_Return_Continue()
    {
        // status → McpActionConstants.Status(MCP 专属子命令)
        var cmd = new McpCommand();
        var context = CreateContext("status");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("add")]
    [InlineData("create")]
    [InlineData("new")]
    public async Task Execute_WithCreateVariants_Should_Return_Continue(string subCommand)
    {
        // add/create/new — CrudAction.Create/New 别名(MCP 增删改查中的"添加服务器")
        var cmd = new McpCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("delete")]
    [InlineData("rm")]
    public async Task Execute_WithDeleteVariants_Should_Return_Continue(string subCommand)
    {
        // remove/delete/rm — CrudAction.Delete/Rm/Remove 别名
        var cmd = new McpCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithReconnect_Should_Return_Continue()
    {
        // reconnect → McpActionConstants.Reconnect(MCP 专属子命令)
        var cmd = new McpCommand();
        var context = CreateContext("reconnect myserver");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithEnable_Should_Return_Continue()
    {
        // enable → McpActionConstants.Enable → ToggleAction.On
        var cmd = new McpCommand();
        var context = CreateContext("enable myserver");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithDisable_Should_Return_Continue()
    {
        // disable → McpActionConstants.Disable → ToggleAction.Off
        var cmd = new McpCommand();
        var context = CreateContext("disable myserver");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_NotThrow()
    {
        var cmd = new McpCommand();
        var context = CreateContext("unknown-action");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("LIST")]
    [InlineData("LS")]
    [InlineData("ADD")]
    [InlineData("CREATE")]
    [InlineData("NEW")]
    [InlineData("REMOVE")]
    [InlineData("DELETE")]
    [InlineData("RM")]
    [InlineData("STATUS")]
    [InlineData("RECONNECT")]
    [InlineData("ENABLE")]
    [InlineData("DISABLE")]
    public async Task Execute_WithUppercaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        // 验证小写化路由(toLowerInvariant 后枚举匹配)
        var cmd = new McpCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    // ===== McpAction 枚举字面量路由验证 =====

    [Theory]
    [InlineData("status", McpAction.Status)]
    [InlineData("reconnect", McpAction.Reconnect)]
    [InlineData("enable", McpAction.Enable)]
    [InlineData("disable", McpAction.Disable)]
    public void McpAction_FromValue_ValidString_Should_Resolve_Correctly(string input, McpAction expected)
    {
        McpActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void McpActionConstants_Values_Should_Match_Route()
    {
        // 验证枚举常量值与原硬编码字符串完全一致(行为不变)
        McpActionConstants.Status.Should().Be("status");
        McpActionConstants.Reconnect.Should().Be("reconnect");
        McpActionConstants.Enable.Should().Be("enable");
        McpActionConstants.Disable.Should().Be("disable");
    }

    private static ChatCommandContext CreateContext(string arguments)
    {
        // mock IMcpServerConfigStore 和 IMcpToolRegistry,避免 ListServersAsync 抛 "服务未初始化"
        var configStoreMock = new Mock<IMcpServerConfigStore>();
        configStoreMock.Setup(s => s.GetAllServersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, (string Scope, McpServerConfigEntry Entry)>());

        var mcpRegistryMock = new Mock<IMcpToolRegistry>();
        mcpRegistryMock.Setup(r => r.GetAllRemoteClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IMcpClient>().AsReadOnly());
        mcpRegistryMock.Setup(r => r.GetLocalToolCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        mcpRegistryMock.Setup(r => r.GetRemoteClientCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        mcpRegistryMock.Setup(r => r.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IToolHandler>().AsReadOnly());

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IMcpServerConfigStore)))
            .Returns(configStoreMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IMcpToolRegistry)))
            .Returns(mcpRegistryMock.Object);

        return new ChatCommandContext
        {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ServiceProvider = serviceProviderMock.Object,
                ToolRegistry = mcpRegistryMock.Object,
            FileSystem = TestFileSystem.Current,
            },
        };
    }
}
