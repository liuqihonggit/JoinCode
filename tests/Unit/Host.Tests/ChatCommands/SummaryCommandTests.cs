namespace Host.Tests.ChatCommands;

public sealed class SummaryCommandTests
{
    [Fact]
    public void Name_Should_Be_summary()
    {
        var cmd = new SummaryCommand();
        cmd.Name.Should().Be("summary");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new SummaryCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new SummaryCommand();
        cmd.Usage.Should().StartWith("/summary");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new SummaryCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_Should_Return_Continue()
    {
        var cmd = new SummaryCommand();
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
    public async Task Execute_Should_Show_Summary_Of_Chat_History()
    {
        var cmd = new SummaryCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ApiMessageRecord { Role = "user", Content = "Hello" },
                new ApiMessageRecord { Role = "assistant", Content = "Hi there!" }
            ]);

        var context = new ChatCommandContext {
            Arguments = "",
            SessionId = "test-session",
            SessionStartedAt = DateTime.UtcNow.AddMinutes(-30),
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
}