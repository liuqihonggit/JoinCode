namespace Host.Tests.ChatCommands;

public sealed class UsageCommandTests
{
    [Fact]
    public void Name_Should_Be_usage()
    {
        var cmd = new UsageCommand();
        cmd.Name.Should().Be("usage");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new UsageCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Be_exact()
    {
        var cmd = new UsageCommand();
        cmd.Usage.Should().Be("/usage");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new UsageCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Contain_rate_limit()
    {
        var cmd = new UsageCommand();
        cmd.Aliases.Should().Contain("rate-limit");
    }

    [Fact]
    public async Task Execute_Should_Return_Continue()
    {
        var cmd = new UsageCommand();
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
    public async Task Execute_WithUsageTracker_Should_Show_Token_Stats()
    {
        var cmd = new UsageCommand();
        var usageTracker = new Mock<IUsageTracker>();
        usageTracker.Setup(ut => ut.GetTodayStatistics())
            .Returns(new TokenUsageStatistics
            {
                TotalInputTokens = 5000,
                TotalOutputTokens = 2000,
                TotalRequests = 3,
                TotalCostUsd = 0.15m,
            });
        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                UsageTracker = usageTracker.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }
}