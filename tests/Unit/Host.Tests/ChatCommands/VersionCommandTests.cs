namespace Host.Tests.ChatCommands;

public sealed class VersionCommandTests
{
    [Fact]
    public void Name_Should_Be_version()
    {
        var cmd = new VersionCommand();
        cmd.Name.Should().Be("version");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new VersionCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new VersionCommand();
        cmd.Usage.Should().StartWith("/version");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new VersionCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_Should_Return_Continue()
    {
        var cmd = new VersionCommand();
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
}