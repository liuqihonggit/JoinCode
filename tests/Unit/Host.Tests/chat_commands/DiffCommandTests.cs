namespace Host.Tests.ChatCommands;

public sealed class DiffCommandTests
{
    private static IGitCommandRunner CreateMockGitRunner()
    {
        var mock = new Mock<IGitCommandRunner>();
        mock.Setup(r => r.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = string.Empty, ExitCode = 0 });
        return mock.Object;
    }

    private static CommandServices CreateCommandServices(IGitCommandRunner gitRunner)
    {
        var services = new ServiceCollection();
        services.AddSingleton(gitRunner);
        return new CommandServices
        {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
        };
    }

    [Fact]
    public void Name_Should_Be_diff()
    {
        var cmd = new DiffCommand();
        cmd.Name.Should().Be("diff");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new DiffCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new DiffCommand();
        cmd.Usage.Should().StartWith("/diff");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new DiffCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new DiffCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Fact]
    public void ArgumentHint_Should_Not_Be_Empty()
    {
        var cmd = new DiffCommand();
        cmd.ArgumentHint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_Default_Should_Return_Continue()
    {
        var cmd = new DiffCommand();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = new ChatCommandContext
        {
            Arguments = "",
            CancellationToken = cts.Token,
            Services = new CommandServiceProvider(CreateCommandServices(CreateMockGitRunner())),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Files_Should_Return_Continue()
    {
        var cmd = new DiffCommand();
        var context = new ChatCommandContext
        {
            Arguments = "files",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(CreateCommandServices(CreateMockGitRunner())),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Cached_Should_Return_Continue()
    {
        var cmd = new DiffCommand();
        var context = new ChatCommandContext
        {
            Arguments = "cached",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(CreateCommandServices(CreateMockGitRunner())),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Staged_Should_Return_Continue()
    {
        var cmd = new DiffCommand();
        var context = new ChatCommandContext
        {
            Arguments = "staged",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(CreateCommandServices(CreateMockGitRunner())),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Theory]
    [InlineData("FILES")]
    [InlineData("CACHED")]
    [InlineData("STAGED")]
    public async Task Execute_WithUppercaseSubCommand_Should_Be_CaseInsensitive(string subCommand)
    {
        var cmd = new DiffCommand();
        var context = new ChatCommandContext
        {
            Arguments = subCommand,
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(CreateCommandServices(CreateMockGitRunner())),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubCommand_Should_Fall_Through_To_Default()
    {
        var cmd = new DiffCommand();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = new ChatCommandContext
        {
            Arguments = "unknown-mode",
            CancellationToken = cts.Token,
            Services = new CommandServiceProvider(CreateCommandServices(CreateMockGitRunner())),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }
}