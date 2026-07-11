namespace Core.Context;

public sealed class ChatUsageProcessorTests
{
    [Fact]
    public async Task ProcessUsageAsync_PassesCacheBreakResultToRecordTurn()
    {
        var stats = new SessionStats();
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(cm => cm.CheckCacheBreakAsync(It.IsAny<PromptStateSnapshot>(), It.IsAny<TokenUsage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CacheBreakResult.Break(CacheBreakKind.ToolSpecsChanged, "tools changed"));
        contextManager.Setup(cm => cm.DecideAfterUsage(It.IsAny<TokenUsage>()))
            .Returns(ContextFoldDecision.None);

        var sut = new ChatUsageProcessor(stats, contextManager.Object);
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var snapshot = new PromptStateSnapshot
        {
            SystemPromptHash = "abc",
            ToolSpecsHash = "def",
            ToolCount = 1,
            ToolNamesHash = "ghi",
            DynamicContentHash = "jkl"
        };

        await sut.ProcessUsageAsync(usage, "gpt-4o", snapshot, CancellationToken.None).ConfigureAwait(true);

        stats.ToolSpecsCacheBreaks.Should().Be(1);
        stats.TurnCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessUsageAsync_NoCacheBreak_NoSectionBreakIncrement()
    {
        var stats = new SessionStats();
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(cm => cm.CheckCacheBreakAsync(It.IsAny<PromptStateSnapshot>(), It.IsAny<TokenUsage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CacheBreakResult.NoBreak());
        contextManager.Setup(cm => cm.DecideAfterUsage(It.IsAny<TokenUsage>()))
            .Returns(ContextFoldDecision.None);

        var sut = new ChatUsageProcessor(stats, contextManager.Object);
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 80, CacheCreationInputTokens = 20 };
        var snapshot = new PromptStateSnapshot
        {
            SystemPromptHash = "abc",
            ToolSpecsHash = "def",
            ToolCount = 1,
            ToolNamesHash = "ghi",
            DynamicContentHash = "jkl"
        };

        await sut.ProcessUsageAsync(usage, "gpt-4o", snapshot, CancellationToken.None).ConfigureAwait(true);

        stats.SystemPromptCacheBreaks.Should().Be(0);
        stats.ToolSpecsCacheBreaks.Should().Be(0);
        stats.DynamicContentCacheBreaks.Should().Be(0);
        stats.CacheEvictionBreaks.Should().Be(0);
        stats.TurnCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessUsageAsync_SystemPromptBreak_IncrementSystemSection()
    {
        var stats = new SessionStats();
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(cm => cm.CheckCacheBreakAsync(It.IsAny<PromptStateSnapshot>(), It.IsAny<TokenUsage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CacheBreakResult.Break(CacheBreakKind.SystemPromptChanged, "system changed"));
        contextManager.Setup(cm => cm.DecideAfterUsage(It.IsAny<TokenUsage>()))
            .Returns(ContextFoldDecision.None);

        var sut = new ChatUsageProcessor(stats, contextManager.Object);
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var snapshot = new PromptStateSnapshot
        {
            SystemPromptHash = "abc",
            ToolSpecsHash = "def",
            ToolCount = 1,
            ToolNamesHash = "ghi",
            DynamicContentHash = "jkl"
        };

        await sut.ProcessUsageAsync(usage, "gpt-4o", snapshot, CancellationToken.None).ConfigureAwait(true);

        stats.SystemPromptCacheBreaks.Should().Be(1);
        stats.ToolSpecsCacheBreaks.Should().Be(0);
    }

    [Fact]
    public async Task ProcessUsageAsync_CacheEvictionBreak_IncrementEvictionSection()
    {
        var stats = new SessionStats();
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(cm => cm.CheckCacheBreakAsync(It.IsAny<PromptStateSnapshot>(), It.IsAny<TokenUsage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CacheBreakResult.Break(CacheBreakKind.CacheEviction, "eviction"));
        contextManager.Setup(cm => cm.DecideAfterUsage(It.IsAny<TokenUsage>()))
            .Returns(ContextFoldDecision.None);

        var sut = new ChatUsageProcessor(stats, contextManager.Object);
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var snapshot = new PromptStateSnapshot
        {
            SystemPromptHash = "abc",
            ToolSpecsHash = "def",
            ToolCount = 1,
            ToolNamesHash = "ghi",
            DynamicContentHash = "jkl"
        };

        await sut.ProcessUsageAsync(usage, "gpt-4o", snapshot, CancellationToken.None).ConfigureAwait(true);

        stats.CacheEvictionBreaks.Should().Be(1);
    }
}
