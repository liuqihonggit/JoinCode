namespace Host.Tests.ChatCommands;

public sealed class BtwCommandTests
{
    [Fact]
    public void Name_Should_Be_btw()
    {
        var cmd = new BtwCommand();
        cmd.Name.Should().Be("btw");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new BtwCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new BtwCommand();
        cmd.Usage.Should().StartWith("/btw");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new BtwCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new BtwCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Fact]
    public void ArgumentHint_Should_Not_Be_Empty()
    {
        var cmd = new BtwCommand();
        cmd.ArgumentHint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Show_Usage()
    {
        var cmd = new BtwCommand();
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
    public async Task Execute_WithWhitespaceArgs_Should_Show_Usage()
    {
        var cmd = new BtwCommand();
        var context = new ChatCommandContext {
            Arguments = "   ",
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
    public async Task Execute_WithQuestion_Should_SendMessage()
    {
        var cmd = new BtwCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test answer");

        var context = new ChatCommandContext {
            Arguments = "test question",
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
        chatService.Verify(c => c.SendMessageAsync(
            It.Is<string>(s => s.Contains("test question")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_WhenSendMessageThrows_Should_Return_Continue()
    {
        var cmd = new BtwCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("chat error"));

        var context = new ChatCommandContext {
            Arguments = "test question",
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
