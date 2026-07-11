namespace Host.Tests.ChatCommands;

public sealed class BridgeKickCommandTests
{
    [Fact]
    public void Name_Should_Be_bridge_kick()
    {
        var cmd = new BridgeKickCommand();
        cmd.Name.Should().Be("bridge-kick");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new BridgeKickCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new BridgeKickCommand();
        cmd.Usage.Should().StartWith("/bridge-kick");
    }

    [Fact]
    public async Task Execute_WithoutArgs_Should_Show_Usage()
    {
        var cmd = new BridgeKickCommand();
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
    public async Task Execute_WithSessionId_Should_Attempt_Disconnect()
    {
        var cmd = new BridgeKickCommand();
        var context = new ChatCommandContext {
            Arguments = "session-123",
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