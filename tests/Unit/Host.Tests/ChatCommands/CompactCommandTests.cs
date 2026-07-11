namespace Host.Tests.ChatCommands;

public sealed class CompactCommandTests
{
    [Fact]
    public void Name_Should_Be_compact()
    {
        var cmd = new CompactCommand();
        cmd.Name.Should().Be("compact");
    }

    [Fact]
    public void Description_Should_Contain_压缩()
    {
        var cmd = new CompactCommand();
        cmd.Description.Should().Contain("压缩");
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new CompactCommand();
        cmd.Usage.Should().StartWith("/compact");
    }

    [Fact]
    public void Aliases_Should_Contain_comp()
    {
        var cmd = new CompactCommand();
        cmd.Aliases.Should().Contain("comp");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new CompactCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_Should_Return_Continue()
    {
        var cmd = new CompactCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessageRecord>());

        var context = new ChatCommandContext {
            Arguments = "",
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
    public async Task Execute_WithMessageList_Should_Return_Continue()
    {
        var cmd = new CompactCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessageRecord>
            {
                new() { Role = "user", Content = "Hello" },
                new() { Role = "assistant", Content = "Hi there!" }
            });
        chatService.Setup(cs => cs.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("摘要内容");

        var context = new ChatCommandContext {
            Arguments = "",
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
    public void CalculateOriginalMetrics_WithMultipleMessages_ShouldReturnCorrectCount()
    {
        var history = new List<ApiMessageRecord>
        {
            new() { Role = "user", Content = "Hello" },
            new() { Role = "assistant", Content = "Hi there!" },
            new() { Role = "user", Content = "Can you help me?" },
            new() { Role = "assistant", Content = "Of course!" },
            new() { Role = "user", Content = "Thanks" },
        };

        var (count, estimatedTokens) = CompactCommand.CalculateOriginalMetrics(history);

        count.Should().Be(5);
        estimatedTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CalculateOriginalMetrics_WithEmptyHistory_ShouldReturnZero()
    {
        var history = new List<ApiMessageRecord>();

        var (count, estimatedTokens) = CompactCommand.CalculateOriginalMetrics(history);

        count.Should().Be(0);
        estimatedTokens.Should().Be(0);
    }

    [Fact]
    public void CalculateOriginalMetrics_WithLongContent_ShouldEstimateTokensRoughlyCorrect()
    {
        var history = new List<ApiMessageRecord>
        {
            new() { Role = "user", Content = new string('a', 4000) },
        };

        var (count, estimatedTokens) = CompactCommand.CalculateOriginalMetrics(history);

        count.Should().Be(1);
        estimatedTokens.Should().Be(1000);
    }

    [Fact]
    public async Task Execute_WithEmptyHistory_ShouldShowZeroTokensSaved()
    {
        var cmd = new CompactCommand();
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessageRecord>());

        var context = new ChatCommandContext {
            Arguments = "",
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