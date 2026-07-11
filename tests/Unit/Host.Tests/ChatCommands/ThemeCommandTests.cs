namespace Host.Tests.ChatCommands;

public sealed class ThemeCommandTests
{
    [Fact]
    public void Name_Should_Be_theme()
    {
        var cmd = new ThemeCommand();
        cmd.Name.Should().Be("theme");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new ThemeCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new ThemeCommand();
        cmd.Usage.Should().StartWith("/theme");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new ThemeCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new ThemeCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_Show_Should_Return_Continue()
    {
        var cmd = new ThemeCommand();
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
    public async Task Execute_Show_With_Invalid_Theme_Should_Return_Continue()
    {
        var cmd = new ThemeCommand();
        var context = new ChatCommandContext {
            Arguments = "invalid",
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