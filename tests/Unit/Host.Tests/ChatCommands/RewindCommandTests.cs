namespace Host.Tests.ChatCommands;

public sealed class RewindCommandTests
{
    [Fact]
    public void Name_Should_Be_rewind()
    {
        var cmd = new RewindCommand();
        cmd.Name.Should().Be("rewind");
    }

    [Fact]
    public void Description_Should_Be_恢复代码和对话到之前的状态()
    {
        var cmd = new RewindCommand();
        cmd.Description.Should().Be("恢复代码和/或对话到之前的状态");
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new RewindCommand();
        cmd.Usage.Should().StartWith("/rewind");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new RewindCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_Default_Should_Rewind_Last_Turn()
    {
        var cmd = new RewindCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 8));

        var context = new ChatCommandContext {
            Arguments = "last",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = chatService.Object,
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
    public async Task Execute_All_Should_Rewind_To_Start()
    {
        var cmd = new RewindCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.RewindToStartAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.ClearHistory, 10, 0));

        var context = new ChatCommandContext {
            Arguments = "all",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = chatService.Object,
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
    public async Task Execute_WithIndex_Should_Rewind_To_Index()
    {
        var cmd = new RewindCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.RewindToMessageIndexAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TruncateToIndex, 5, 3));

        var context = new ChatCommandContext {
            Arguments = "3",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = chatService.Object,
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
    public async Task Execute_UnknownArg_Should_Show_Usage_And_Return_Continue()
    {
        var cmd = new RewindCommand();
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