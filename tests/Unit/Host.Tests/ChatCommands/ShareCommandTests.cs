namespace Host.Tests.ChatCommands;

public sealed class ShareCommandTests
{
    [Fact]
    public void Name_Should_Be_share()
    {
        var cmd = new ShareCommand();
        cmd.Name.Should().Be("share");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new ShareCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new ShareCommand();
        cmd.Usage.Should().StartWith("/share");
    }

    [Fact]
    public async Task Execute_Should_Return_Continue()
    {
        var cmd = new ShareCommand();
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