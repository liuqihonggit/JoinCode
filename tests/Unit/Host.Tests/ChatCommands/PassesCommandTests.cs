namespace Host.Tests.ChatCommands;

public sealed class PassesCommandTests
{
    [Fact]
    public void Name_Should_Be_passes()
    {
        var cmd = new PassesCommand();
        cmd.Name.Should().Be("passes");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new PassesCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new PassesCommand();
        cmd.Usage.Should().StartWith("/passes");
    }

    [Fact]
    public void IsHidden_Should_Be_True()
    {
        var cmd = new PassesCommand();
        cmd.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new PassesCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Fact]
    public void ArgumentHint_Should_Be_Empty()
    {
        var cmd = new PassesCommand();
        cmd.ArgumentHint.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_Should_Return_Continue()
    {
        var cmd = new PassesCommand();
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
    public async Task Execute_WithArgs_Should_Return_Continue()
    {
        var cmd = new PassesCommand();
        var context = new ChatCommandContext {
            Arguments = "grant test-agent",
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
}
