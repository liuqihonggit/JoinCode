namespace Host.Tests.ChatCommands;

public sealed class IdeCommandTests
{
    [Fact]
    public void Name_Should_Be_ide()
    {
        var cmd = new IdeCommand();
        cmd.Name.Should().Be("ide");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new IdeCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Contain_detect()
    {
        var cmd = new IdeCommand();
        cmd.Usage.Should().Contain("detect");
    }

    [Fact]
    public void IsHidden_Should_Be_True()
    {
        var cmd = new IdeCommand();
        cmd.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void ArgumentHint_Should_Contain_detect()
    {
        var cmd = new IdeCommand();
        cmd.ArgumentHint.Should().Contain("detect");
    }

    [Fact]
    public async Task Execute_WhenServiceIsNull_Should_Return_Continue()
    {
        var cmd = new IdeCommand();
        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Detect_Should_Call_DetectInstalledIdesDetailed()
    {
        var cmd = new IdeCommand();
        var ideService = new Mock<IIdeIntegrationService>();
        ideService.Setup(s => s.DetectInstalledIdesDetailed())
            .Returns(new List<IdeDetectionDetail>
            {
                new() { Type = IdeType.VsCode, Name = "Visual Studio Code", FoundOnPath = true, Path = "C:\\code.exe", IsRunning = false, ExtensionInstalled = true },
            }.AsReadOnly());

        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(IIdeIntegrationService))).Returns(ideService.Object);

        var context = new ChatCommandContext {
            Arguments = "detect",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ServiceProvider = sp.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        ideService.Verify(s => s.DetectInstalledIdesDetailed(), Times.Once);
    }

    [Fact]
    public async Task Execute_Status_Should_Return_Continue()
    {
        var cmd = new IdeCommand();
        var ideService = new Mock<IIdeIntegrationService>();
        ideService.Setup(s => s.CurrentConnection).Returns((IdeInfo?)null);
        ideService.Setup(s => s.DetectInstalledIdes())
            .Returns(Array.Empty<IdeInfo>().ToList().AsReadOnly());

        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(IIdeIntegrationService))).Returns(ideService.Object);

        var context = new ChatCommandContext {
            Arguments = "status",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ServiceProvider = sp.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    // ===== PlatformAction 枚举路由测试(Step 7 重构后) =====

    [Theory]
    [InlineData("detect")]
    [InlineData("connect")]
    [InlineData("disconnect")]
    [InlineData("open")]
    [InlineData("status")]
    public async Task Execute_WithPlatformActionSubcommand_Should_Return_Continue(string subCommand)
    {
        // PlatformActionConstants.Detect/Connect/Disconnect/Open/Status 枚举路由取值范围测试
        var ideService = new Mock<IIdeIntegrationService>();
        ideService.Setup(s => s.DetectInstalledIdesDetailed())
            .Returns(new List<IdeDetectionDetail>().AsReadOnly());
        ideService.Setup(s => s.CurrentConnection).Returns((IdeInfo?)null);
        ideService.Setup(s => s.DetectInstalledIdes())
            .Returns(Array.Empty<IdeInfo>().ToList().AsReadOnly());

        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(IIdeIntegrationService))).Returns(ideService.Object);

        var cmd = new IdeCommand();
        var context = new ChatCommandContext
        {
            Arguments = subCommand,
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ServiceProvider = sp.Object,
            FileSystem = TestFileSystem.Current,
            },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("c")]
    [InlineData("d")]
    [InlineData("o")]
    [InlineData("s")]
    public async Task Execute_WithSingleLetterAlias_Should_Return_Continue(string alias)
    {
        // 单字母别名: c/d/o/s 保留为字符串 case,但应路由到相同 handler
        var ideService = new Mock<IIdeIntegrationService>();
        ideService.Setup(s => s.CurrentConnection).Returns((IdeInfo?)null);
        ideService.Setup(s => s.DetectInstalledIdes())
            .Returns(Array.Empty<IdeInfo>().ToList().AsReadOnly());

        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(IIdeIntegrationService))).Returns(ideService.Object);

        var cmd = new IdeCommand();
        var context = new ChatCommandContext
        {
            Arguments = alias,
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ServiceProvider = sp.Object,
            FileSystem = TestFileSystem.Current,
            },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Default_To_Status()
    {
        // "" → PlatformActionConstants.Status 分支
        var ideService = new Mock<IIdeIntegrationService>();
        ideService.Setup(s => s.CurrentConnection).Returns((IdeInfo?)null);
        ideService.Setup(s => s.DetectInstalledIdes())
            .Returns(Array.Empty<IdeInfo>().ToList().AsReadOnly());

        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(IIdeIntegrationService))).Returns(ideService.Object);

        var cmd = new IdeCommand();
        var context = new ChatCommandContext
        {
            Arguments = "",
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ServiceProvider = sp.Object,
            FileSystem = TestFileSystem.Current,
            },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("DETECT")]
    [InlineData("Connect")]
    [InlineData("DISCONNECT")]
    [InlineData("Open")]
    [InlineData("STATUS")]
    public async Task Execute_WithUppercaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        var ideService = new Mock<IIdeIntegrationService>();
        ideService.Setup(s => s.DetectInstalledIdesDetailed())
            .Returns(new List<IdeDetectionDetail>().AsReadOnly());
        ideService.Setup(s => s.CurrentConnection).Returns((IdeInfo?)null);
        ideService.Setup(s => s.DetectInstalledIdes())
            .Returns(Array.Empty<IdeInfo>().ToList().AsReadOnly());

        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(IIdeIntegrationService))).Returns(ideService.Object);

        var cmd = new IdeCommand();
        var context = new ChatCommandContext
        {
            Arguments = subCommand,
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ServiceProvider = sp.Object,
            FileSystem = TestFileSystem.Current,
            },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_NotThrow()
    {
        var ideService = new Mock<IIdeIntegrationService>();
        ideService.Setup(s => s.CurrentConnection).Returns((IdeInfo?)null);
        ideService.Setup(s => s.DetectInstalledIdes())
            .Returns(Array.Empty<IdeInfo>().ToList().AsReadOnly());

        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(IIdeIntegrationService))).Returns(ideService.Object);

        var cmd = new IdeCommand();
        var context = new ChatCommandContext
        {
            Arguments = "unknown-action",
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ServiceProvider = sp.Object,
            FileSystem = TestFileSystem.Current,
            },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    // ===== PlatformAction 枚举字面量路由验证 =====

    [Theory]
    [InlineData("detect", PlatformAction.Detect)]
    [InlineData("connect", PlatformAction.Connect)]
    [InlineData("disconnect", PlatformAction.Disconnect)]
    [InlineData("open", PlatformAction.Open)]
    [InlineData("status", PlatformAction.Status)]
    public void PlatformAction_FromValue_IdeActions_Should_Resolve_Correctly(string input, PlatformAction expected)
    {
        PlatformActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void PlatformActionConstants_IdeActions_Values_Should_Match_Route()
    {
        // 验证枚举常量值与原硬编码字符串完全一致(行为不变)
        PlatformActionConstants.Detect.Should().Be("detect");
        PlatformActionConstants.Connect.Should().Be("connect");
        PlatformActionConstants.Disconnect.Should().Be("disconnect");
        PlatformActionConstants.Open.Should().Be("open");
        PlatformActionConstants.Status.Should().Be("status");
    }
}
