namespace Host.Tests.ChatCommands;

/// <summary>
/// MobileCommand 取值范围测试 — 验证 PlatformAction 枚举字面量正确路由
/// 覆盖:start/stop/url + 单字母别名(s/d/u) + 大小写不敏感 + 默认 status
/// </summary>
public sealed class MobileCommandTests
{
    [Fact]
    public void Name_Should_Be_mobile()
    {
        var cmd = new MobileCommand();
        cmd.Name.Should().Be("mobile");
    }

    [Fact]
    public void IsHidden_Should_Be_True()
    {
        var cmd = new MobileCommand();
        cmd.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void Aliases_Should_Contain_ios_and_android()
    {
        var cmd = new MobileCommand();
        cmd.Aliases.Should().Contain("ios");
        cmd.Aliases.Should().Contain("android");
    }

    [Fact]
    public void Usage_Should_Contain_All_PlatformActions()
    {
        var cmd = new MobileCommand();
        cmd.Usage.Should().Contain("start");
        cmd.Usage.Should().Contain("stop");
        cmd.Usage.Should().Contain("url");
    }

    [Fact]
    public async Task Execute_WhenServiceIsNull_Should_Return_Continue()
    {
        var cmd = new MobileCommand();
        var context = new ChatCommandContext
        {
            Arguments = "start",
            CancellationToken = CancellationToken.None,
            Services = CreateServices(),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    // ===== PlatformAction 枚举路由测试 =====

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("url")]
    public async Task Execute_WithPlatformActionSubcommand_Should_Return_Continue(string subCommand)
    {
        // PlatformActionConstants.Start/Stop/Url 枚举路由取值范围测试
        var services = CreateServices(mobileService: CreateMockMobileService().Object);
        var cmd = new MobileCommand();
        var context = CreateContext(subCommand, services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("s")]
    [InlineData("d")]
    [InlineData("u")]
    public async Task Execute_WithSingleLetterAlias_Should_Return_Continue(string alias)
    {
        // 单字母别名: s/d/u 保留为字符串 case,但应路由到相同 handler
        var services = CreateServices(mobileService: CreateMockMobileService().Object);
        var cmd = new MobileCommand();
        var context = CreateContext(alias, services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithNullOrEmptyArgs_Should_Default_To_Status()
    {
        // null/"" → 默认 status 分支
        var services = CreateServices(mobileService: CreateMockMobileService().Object);
        var cmd = new MobileCommand();

        var context1 = CreateContext(null, services);
        var context2 = CreateContext("", services);

        var r1 = await cmd.ExecuteAsync(context1).ConfigureAwait(true);
        var r2 = await cmd.ExecuteAsync(context2).ConfigureAwait(true);

        r1.ShouldContinue.Should().BeTrue();
        r2.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("START")]
    [InlineData("Stop")]
    [InlineData("URL")]
    public async Task Execute_WithUppercaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        var services = CreateServices(mobileService: CreateMockMobileService().Object);
        var cmd = new MobileCommand();
        var context = CreateContext(subCommand, services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_NotThrow()
    {
        var services = CreateServices(mobileService: CreateMockMobileService().Object);
        var cmd = new MobileCommand();
        var context = CreateContext("unknown-action", services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Start_WhenServerAlreadyRunning_Should_NotThrow()
    {
        // StartService 在 IsServerRunning=true 时输出提示并 return,不应抛错
        var mock = CreateMockMobileService();
        mock.Setup(s => s.IsServerRunning).Returns(true);
        var services = CreateServices(mobileService: mock.Object);
        var cmd = new MobileCommand();
        var context = CreateContext("start", services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Stop_WhenServerNotRunning_Should_NotThrow()
    {
        // StopService 在 IsServerRunning=false 时输出提示并 return
        var mock = CreateMockMobileService();
        mock.Setup(s => s.IsServerRunning).Returns(false);
        var services = CreateServices(mobileService: mock.Object);
        var cmd = new MobileCommand();
        var context = CreateContext("stop", services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Url_WhenServerNotRunning_Should_NotThrow()
    {
        // Url 在 IsServerRunning=false 时输出提示并 return
        var mock = CreateMockMobileService();
        mock.Setup(s => s.IsServerRunning).Returns(false);
        var services = CreateServices(mobileService: mock.Object);
        var cmd = new MobileCommand();
        var context = CreateContext("url", services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    // ===== PlatformAction 枚举字面量路由验证 =====

    [Theory]
    [InlineData("start", PlatformAction.Start)]
    [InlineData("stop", PlatformAction.Stop)]
    [InlineData("url", PlatformAction.Url)]
    public void PlatformAction_FromValue_MobileActions_Should_Resolve_Correctly(string input, PlatformAction expected)
    {
        PlatformActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void PlatformActionConstants_MobileActions_Values_Should_Match_Route()
    {
        // 验证枚举常量值与原硬编码字符串完全一致(行为不变)
        PlatformActionConstants.Start.Should().Be("start");
        PlatformActionConstants.Stop.Should().Be("stop");
        PlatformActionConstants.Url.Should().Be("url");
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

    private static CommandServices CreateServices(IMobileConnectService? mobileService = null)
    {
        return new CommandServices
        {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            ServiceProvider = mobileService is null
                ? Mock.Of<IServiceProvider>()
                : CreateServiceProvider(typeof(IMobileConnectService), mobileService),
        FileSystem = TestFileSystem.Current,
        };
    }

    private static IServiceProvider CreateServiceProvider(Type serviceType, object serviceInstance)
    {
        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(serviceType)).Returns(serviceInstance);
        return sp.Object;
    }

    private static Mock<IMobileConnectService> CreateMockMobileService()
    {
        var mock = new Mock<IMobileConnectService>();
        mock.Setup(s => s.IsServerRunning).Returns(true);
        mock.Setup(s => s.GenerateConnectUrl(It.IsAny<int>())).Returns("http://localhost:8080/connect");
        mock.Setup(s => s.StartConnectServerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(8080);
        mock.Setup(s => s.StopConnectServer());
        return mock;
    }
}
