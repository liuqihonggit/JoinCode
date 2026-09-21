namespace Integration.Tests.PrefixCache.Unit;

public sealed class SessionStatsSectionBreakTests {
    [Fact]
    public async Task RecordTurn_WithCacheBreak_IncrementsSectionBreakCount() {
        await using var stats = new SessionStats();
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var breakResult = CacheBreakResult.Break(CacheBreakKind.SystemPromptChanged, "system changed");

        stats.RecordTurn(usage, 0, breakResult);

        stats.SystemPromptCacheBreaks.Should().Be(1);
        stats.ToolSpecsCacheBreaks.Should().Be(0);
        stats.DynamicContentCacheBreaks.Should().Be(0);
    }

    [Fact]
    public async Task RecordTurn_WithToolSpecsBreak_IncrementsToolSpecsBreakCount() {
        await using var stats = new SessionStats();
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var breakResult = CacheBreakResult.Break(CacheBreakKind.ToolSpecsChanged, "tools changed");

        stats.RecordTurn(usage, 0, breakResult);

        stats.ToolSpecsCacheBreaks.Should().Be(1);
        stats.SystemPromptCacheBreaks.Should().Be(0);
    }

    [Fact]
    public async Task RecordTurn_WithDynamicBreak_IncrementsDynamicBreakCount() {
        await using var stats = new SessionStats();
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var breakResult = CacheBreakResult.Break(CacheBreakKind.DynamicContentChanged, "dynamic changed");

        stats.RecordTurn(usage, 0, breakResult);

        stats.DynamicContentCacheBreaks.Should().Be(1);
    }

    [Fact]
    public async Task RecordTurn_WithoutCacheBreak_NoIncrement() {
        await using var stats = new SessionStats();
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 80, CacheCreationInputTokens = 20 };

        stats.RecordTurn(usage, 0, null);

        stats.SystemPromptCacheBreaks.Should().Be(0);
        stats.ToolSpecsCacheBreaks.Should().Be(0);
        stats.DynamicContentCacheBreaks.Should().Be(0);
    }

    [Fact]
    public async Task RecordTurn_MultipleBreaks_Accumulate() {
        await using var stats = new SessionStats();
        var usage1 = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var usage2 = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };

        stats.RecordTurn(usage1, 0, CacheBreakResult.Break(CacheBreakKind.SystemPromptChanged, "s1"));
        stats.RecordTurn(usage2, 0, CacheBreakResult.Break(CacheBreakKind.ToolSpecsChanged, "t1"));

        stats.SystemPromptCacheBreaks.Should().Be(1);
        stats.ToolSpecsCacheBreaks.Should().Be(1);
        stats.TurnCount.Should().Be(2);
    }

    [Fact]
    public async Task RecordTurn_CacheEviction_CountsAsBreak() {
        await using var stats = new SessionStats();
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var breakResult = CacheBreakResult.Break(CacheBreakKind.CacheEviction, "eviction");

        stats.RecordTurn(usage, 0, breakResult);

        stats.CacheEvictionBreaks.Should().Be(1);
    }

    [Fact]
    public async Task Reset_ClearsAllSectionBreaks() {
        await using var stats = new SessionStats();
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };

        stats.RecordTurn(usage, 0, CacheBreakResult.Break(CacheBreakKind.SystemPromptChanged, "s1"));
        stats.RecordTurn(usage, 0, CacheBreakResult.Break(CacheBreakKind.ToolSpecsChanged, "t1"));
        stats.Reset();

        stats.SystemPromptCacheBreaks.Should().Be(0);
        stats.ToolSpecsCacheBreaks.Should().Be(0);
        stats.DynamicContentCacheBreaks.Should().Be(0);
        stats.CacheEvictionBreaks.Should().Be(0);
    }

    [Fact]
    public async Task RecordTurn_ExistingOverload_StillWorks() {
        await using var stats = new SessionStats();
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 80, CacheCreationInputTokens = 20 };

        stats.RecordTurn(usage, 0.01m);

        stats.TotalCacheHitTokens.Should().Be(80);
        stats.TotalCacheMissTokens.Should().Be(20);
        stats.TurnCount.Should().Be(1);
    }
}