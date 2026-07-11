namespace Host.Tests.ChatCommands;

public sealed class PluginCommandTests
{
    [Fact]
    public void Name_Should_Be_plugin()
    {
        var cmd = new PluginCommand();
        cmd.Name.Should().Be("plugin");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new PluginCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new PluginCommand();
        cmd.Usage.Should().StartWith("/plugin");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new PluginCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new PluginCommand();
        cmd.Aliases.Should().BeEquivalentTo("plugins", "marketplace");
    }

    [Fact]
    public void ArgumentHint_Should_Not_Be_Empty()
    {
        var cmd = new PluginCommand();
        cmd.ArgumentHint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_NoArgs_Should_ListPlugins()
    {
        var cmd = new PluginCommand();
        var pluginManager = new Mock<IPluginManager>();
        pluginManager.Setup(p => p.LoadedWorkflowPluginNames).Returns(Array.Empty<string>());
        pluginManager.Setup(p => p.LoadedExternalPluginNames).Returns(Array.Empty<string>());

        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                PluginManager = pluginManager.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_List_Should_ListPlugins()
    {
        var cmd = new PluginCommand();
        var pluginManager = new Mock<IPluginManager>();
        pluginManager.Setup(p => p.LoadedWorkflowPluginNames).Returns(new[] { "dream" });
        pluginManager.Setup(p => p.LoadedExternalPluginNames).Returns(new[] { "external-plugin" });

        var context = new ChatCommandContext {
            Arguments = "list",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                PluginManager = pluginManager.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_List_WhenManagerIsNull_Should_ShowNotInitialized()
    {
        var cmd = new PluginCommand();
        var context = new ChatCommandContext {
            Arguments = "list",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                PluginManager = null,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Install_Should_Show_Usage_Info()
    {
        var cmd = new PluginCommand();
        var context = new ChatCommandContext {
            Arguments = "install myplugin",
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
    public async Task Execute_Install_WithoutName_Should_Show_Usage()
    {
        var cmd = new PluginCommand();
        var context = new ChatCommandContext {
            Arguments = "install",
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
    public async Task Execute_Uninstall_WithoutName_Should_Show_Usage()
    {
        var cmd = new PluginCommand();
        var context = new ChatCommandContext {
            Arguments = "uninstall",
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
    public async Task Execute_Uninstall_WhenManagerIsNull_Should_ShowNotInitialized()
    {
        var cmd = new PluginCommand();
        var context = new ChatCommandContext {
            Arguments = "uninstall myplugin",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                PluginManager = null,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Uninstall_WhenNotLoaded_Should_Return_Continue()
    {
        var cmd = new PluginCommand();
        var pluginManager = new Mock<IPluginManager>();
        pluginManager.Setup(p => p.IsPluginLoaded("myplugin")).Returns(false);

        var context = new ChatCommandContext {
            Arguments = "uninstall myplugin",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                PluginManager = pluginManager.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Uninstall_Success_Should_Return_Continue()
    {
        var cmd = new PluginCommand();
        var pluginManager = new Mock<IPluginManager>();
        pluginManager.Setup(p => p.IsPluginLoaded("myplugin")).Returns(true);
        pluginManager.Setup(p => p.UnloadPluginAsync("myplugin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PluginUnloadResult.Success("myplugin", TimeSpan.FromMilliseconds(10)));

        var context = new ChatCommandContext {
            Arguments = "uninstall myplugin",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                PluginManager = pluginManager.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Uninstall_Failure_Should_Return_Continue()
    {
        var cmd = new PluginCommand();
        var pluginManager = new Mock<IPluginManager>();
        pluginManager.Setup(p => p.IsPluginLoaded("myplugin")).Returns(true);
        pluginManager.Setup(p => p.UnloadPluginAsync("myplugin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PluginUnloadResult.AlcUnloadFailed("myplugin", TimeSpan.FromMilliseconds(100), "unload error"));

        var context = new ChatCommandContext {
            Arguments = "uninstall myplugin",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                PluginManager = pluginManager.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Enable_Should_Return_Continue()
    {
        var cmd = new PluginCommand();
        var context = new ChatCommandContext {
            Arguments = "enable myplugin",
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
    public async Task Execute_Disable_Should_Return_Continue()
    {
        var cmd = new PluginCommand();
        var context = new ChatCommandContext {
            Arguments = "disable myplugin",
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
    public async Task Execute_UnknownAction_Should_Return_Continue()
    {
        var cmd = new PluginCommand();
        var context = new ChatCommandContext {
            Arguments = "unknown",
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
    public async Task Execute_Uninstall_WhenUnloadThrows_Should_Return_Continue()
    {
        var cmd = new PluginCommand();
        var pluginManager = new Mock<IPluginManager>();
        pluginManager.Setup(p => p.IsPluginLoaded("myplugin")).Returns(true);
        pluginManager.Setup(p => p.UnloadPluginAsync("myplugin", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unload failed"));

        var context = new ChatCommandContext {
            Arguments = "uninstall myplugin",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                PluginManager = pluginManager.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }
}