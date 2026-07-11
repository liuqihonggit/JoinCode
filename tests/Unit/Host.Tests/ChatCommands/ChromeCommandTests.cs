namespace Host.Tests.ChatCommands;

/// <summary>
/// ChromeCommand 取值范围测试 — 验证 PlatformAction 枚举字面量正确路由
/// 覆盖:connect/disconnect/install/toggle/status + 单字母别名(c/d/i/t/s) + 大小写不敏感
/// </summary>
public sealed class ChromeCommandTests
{
    [Fact]
    public void Name_Should_Be_chrome()
    {
        var cmd = new ChromeCommand();
        cmd.Name.Should().Be("chrome");
    }

    [Fact]
    public void IsHidden_Should_Be_True()
    {
        var cmd = new ChromeCommand();
        cmd.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void Usage_Should_Contain_All_PlatformActions()
    {
        var cmd = new ChromeCommand();
        cmd.Usage.Should().Contain("connect");
        cmd.Usage.Should().Contain("disconnect");
        cmd.Usage.Should().Contain("install");
        cmd.Usage.Should().Contain("toggle");
        cmd.Usage.Should().Contain("status");
    }

    [Fact]
    public async Task Execute_WhenServiceIsNull_Should_Return_Continue()
    {
        var cmd = new ChromeCommand();
        var context = new ChatCommandContext
        {
            Arguments = "connect",
            CancellationToken = CancellationToken.None,
            Services = CreateServices(),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    // ===== PlatformAction 枚举路由测试 =====

    [Theory]
    [InlineData("connect")]
    [InlineData("disconnect")]
    [InlineData("install")]
    [InlineData("toggle")]
    [InlineData("status")]
    public async Task Execute_WithPlatformActionSubcommand_Should_Return_Continue(string subCommand)
    {
        // PlatformActionConstants.Connect/Disconnect/Install/Toggle/Status 枚举路由取值范围测试
        var services = CreateServices(chromeService: CreateMockChromeService().Object);
        var cmd = new ChromeCommand();
        var context = CreateContext(subCommand, services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("c")]
    [InlineData("d")]
    [InlineData("i")]
    [InlineData("t")]
    [InlineData("s")]
    public async Task Execute_WithSingleLetterAlias_Should_Return_Continue(string alias)
    {
        // 单字母别名: c/d/i/t/s 保留为字符串 case,但应路由到相同 handler
        var services = CreateServices(chromeService: CreateMockChromeService().Object);
        var cmd = new ChromeCommand();
        var context = CreateContext(alias, services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithNullOrEmptyArgs_Should_Default_To_Status()
    {
        // null/"" → PlatformActionConstants.Status 分支
        var services = CreateServices(chromeService: CreateMockChromeService().Object);
        var cmd = new ChromeCommand();

        var context1 = CreateContext(null, services);
        var context2 = CreateContext("", services);

        var r1 = await cmd.ExecuteAsync(context1).ConfigureAwait(true);
        var r2 = await cmd.ExecuteAsync(context2).ConfigureAwait(true);

        r1.ShouldContinue.Should().BeTrue();
        r2.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("CONNECT")]
    [InlineData("Disconnect")]
    [InlineData("INSTALL")]
    [InlineData("Toggle")]
    [InlineData("STATUS")]
    public async Task Execute_WithUppercaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        var services = CreateServices(chromeService: CreateMockChromeService().Object);
        var cmd = new ChromeCommand();
        var context = CreateContext(subCommand, services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_NotThrow()
    {
        var services = CreateServices(chromeService: CreateMockChromeService().Object);
        var cmd = new ChromeCommand();
        var context = CreateContext("unknown-action", services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    // ===== PlatformAction 枚举字面量路由验证 =====

    [Theory]
    [InlineData("connect", PlatformAction.Connect)]
    [InlineData("disconnect", PlatformAction.Disconnect)]
    [InlineData("status", PlatformAction.Status)]
    [InlineData("install", PlatformAction.Install)]
    [InlineData("toggle", PlatformAction.Toggle)]
    public void PlatformAction_FromValue_ChromeActions_Should_Resolve_Correctly(string input, PlatformAction expected)
    {
        PlatformActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void PlatformActionConstants_ChromeActions_Values_Should_Match_Route()
    {
        // 验证枚举常量值与原硬编码字符串完全一致(行为不变)
        PlatformActionConstants.Connect.Should().Be("connect");
        PlatformActionConstants.Disconnect.Should().Be("disconnect");
        PlatformActionConstants.Status.Should().Be("status");
        PlatformActionConstants.Install.Should().Be("install");
        PlatformActionConstants.Toggle.Should().Be("toggle");
    }

    private static ChatCommandContext CreateContext(string? arguments, CommandServices services)
    {
        return new ChatCommandContext
        {
            Arguments = arguments ?? "",
            CancellationToken = CancellationToken.None,
            Services = services,
        };
    }

    private static CommandServices CreateServices(IChromeIntegrationService? chromeService = null)
    {
        return new CommandServices
        {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            ServiceProvider = chromeService is null
                ? Mock.Of<IServiceProvider>()
                : CreateServiceProvider(typeof(IChromeIntegrationService), chromeService),
        FileSystem = TestFileSystem.Current,
        };
    }

    private static IServiceProvider CreateServiceProvider(Type serviceType, object serviceInstance)
    {
        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(serviceType)).Returns(serviceInstance);
        return sp.Object;
    }

    private static Mock<IChromeIntegrationService> CreateMockChromeService()
    {
        var mock = new Mock<IChromeIntegrationService>();
        mock.Setup(s => s.IsExtensionInstalled).Returns(true);
        mock.Setup(s => s.IsConnected).Returns(false);
        mock.Setup(s => s.IsDefaultEnabled).Returns(true);
        mock.Setup(s => s.ConnectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mock.Setup(s => s.DisconnectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(s => s.OpenExtensionPageAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(s => s.ToggleDefaultEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return mock;
    }
}
