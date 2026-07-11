namespace Host.Tests.ChatCommands;

public sealed class CopyCommandTests
{
    [Fact]
    public void Name_Should_Be_copy()
    {
        var cmd = new CopyCommand();
        cmd.Name.Should().Be("copy");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new CopyCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new CopyCommand();
        cmd.Usage.Should().StartWith("/copy");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new CopyCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new CopyCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_WhenClipboardServiceIsNull_Should_Return_Continue()
    {
        var cmd = new CopyCommand();
        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ClipboardService = null,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WhenNoAssistantMessage_Should_Return_Continue()
    {
        var cmd = new CopyCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessageRecord>
            {
                new() { Role = "user", Content = "Hello" }
            }.AsReadOnly());

        var clipboardService = new Mock<IClipboardService>();

        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = chatService.Object,
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ClipboardService = clipboardService.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithAssistantMessage_Should_Call_SetTextAsync()
    {
        var cmd = new CopyCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessageRecord>
            {
                new() { Role = "user", Content = "Hello" },
                new() { Role = "assistant", Content = "Hi there!" }
            }.AsReadOnly());

        var clipboardService = new Mock<IClipboardService>();
        clipboardService.Setup(cs => cs.SetTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = chatService.Object,
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ClipboardService = clipboardService.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        clipboardService.Verify(cs => cs.SetTextAsync("Hi there!", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_WithMultipleAssistantMessages_Should_Copy_LastOne()
    {
        var cmd = new CopyCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessageRecord>
            {
                new() { Role = "user", Content = "Q1" },
                new() { Role = "assistant", Content = "A1" },
                new() { Role = "user", Content = "Q2" },
                new() { Role = "assistant", Content = "A2" }
            }.AsReadOnly());

        var clipboardService = new Mock<IClipboardService>();
        clipboardService.Setup(cs => cs.SetTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = chatService.Object,
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ClipboardService = clipboardService.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        clipboardService.Verify(cs => cs.SetTextAsync("A2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_WhenClipboardThrows_Should_Return_Continue()
    {
        var cmd = new CopyCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessageRecord>
            {
                new() { Role = "assistant", Content = "test" }
            }.AsReadOnly());

        var clipboardService = new Mock<IClipboardService>();
        clipboardService.Setup(cs => cs.SetTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Clipboard error"));

        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = chatService.Object,
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ClipboardService = clipboardService.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }
}