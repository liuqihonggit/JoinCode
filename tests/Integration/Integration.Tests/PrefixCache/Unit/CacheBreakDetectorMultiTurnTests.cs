namespace Integration.Tests.PrefixCache.Unit;

public sealed class CacheBreakDetectorMultiTurnTests
{
    private readonly CacheBreakDetector _detector = new();

    [Fact]
    public void MultiTurn_FirstTurn_CacheCreation_SecondTurn_CacheHit_ThirdTurn_ModelChange_Break()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System prompt", tools, []);

        var snapshot = _detector.RecordPromptState(prefix, "dynamic", modelId: "claude-sonnet-4-20250514");

        var usage1 = new TokenUsage(1000, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 800
        };
        var result1 = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", usage1, currentModelId: "claude-sonnet-4-20250514");
        result1.BreakDetected.Should().BeFalse("first turn: cache creation is expected, not a break");

        var snapshot2 = _detector.RecordPromptState(prefix, "dynamic", modelId: "claude-sonnet-4-20250514");

        var usage2 = new TokenUsage(1000, 50)
        {
            CacheReadInputTokens = 800,
            CacheCreationInputTokens = 0
        };
        var result2 = _detector.CheckCacheBreak(snapshot2, prefix, "dynamic", usage2, currentModelId: "claude-sonnet-4-20250514");
        result2.BreakDetected.Should().BeFalse("second turn: cache hit is expected");

        var snapshot3 = _detector.RecordPromptState(prefix, "dynamic", modelId: "claude-sonnet-4-20250514");

        var usage3 = new TokenUsage(1000, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 1000
        };
        var result3 = _detector.CheckCacheBreak(snapshot3, prefix, "dynamic", usage3, currentModelId: "claude-haiku-4-20250514");
        result3.BreakDetected.Should().BeTrue("third turn: model change should break cache");
        result3.Kind.Should().Be(CacheBreakKind.ModelChanged);
    }

    [Fact]
    public void MultiTurn_ToolAppend_CacheHit_NoBreak_Then_ToolRemove_Break()
    {
        var tools1 = new List<ToolSpec> { new("read", "Read files") };
        var prefix1 = new ImmutablePrefix("System", tools1, []);

        var snapshot1 = _detector.RecordPromptState(prefix1, "dynamic");

        var usage1 = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 80,
            CacheCreationInputTokens = 0
        };
        var result1 = _detector.CheckCacheBreak(snapshot1, prefix1, "dynamic", usage1);
        result1.BreakDetected.Should().BeFalse("first turn: cache hit with original tools");

        var tools2 = new List<ToolSpec> { new("read", "Read files"), new("write", "Write files") };
        var prefix2 = new ImmutablePrefix("System", tools2, []);

        var snapshot2 = _detector.RecordPromptState(prefix2, "dynamic");

        var usage2 = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 80,
            CacheCreationInputTokens = 20
        };
        var result2 = _detector.CheckCacheBreak(snapshot2, prefix2, "dynamic", usage2);
        result2.BreakDetected.Should().BeFalse("second turn: append-only tool change with cache hit is cache-safe");

        var tools3 = new List<ToolSpec> { new("read", "Read files") };
        var prefix3 = new ImmutablePrefix("System", tools3, []);

        var usage3 = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 100
        };
        var result3 = _detector.CheckCacheBreak(snapshot2, prefix3, "dynamic", usage3);
        result3.BreakDetected.Should().BeTrue("third turn: tool removal compared to previous snapshot is not cache-safe");
        result3.Kind.Should().Be(CacheBreakKind.ToolSpecsChanged);
    }

    [Fact]
    public void MultiTurn_CacheEviction_IdenticalPrefix_CacheMissAfterHit()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System", tools, []);

        var snapshot1 = _detector.RecordPromptState(prefix, "dynamic");

        var usage1 = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 80,
            CacheCreationInputTokens = 0
        };
        _detector.CheckCacheBreak(snapshot1, prefix, "dynamic", usage1);

        var snapshot2 = _detector.RecordPromptState(prefix, "dynamic");

        var usage2 = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 100
        };
        var result2 = _detector.CheckCacheBreak(snapshot2, prefix, "dynamic", usage2);
        result2.BreakDetected.Should().BeTrue("cache eviction: identical prefix but cache miss after previous hit");
        result2.Kind.Should().Be(CacheBreakKind.CacheEviction);
    }

    [Fact]
    public void MultiTurn_FastModeToggle_BreaksCache()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System", tools, []);

        var snapshot = _detector.RecordPromptState(prefix, "dynamic", modelId: "claude-sonnet-4-20250514", fastMode: false);

        var usage1 = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 80,
            CacheCreationInputTokens = 0
        };
        var result1 = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", usage1, currentModelId: "claude-sonnet-4-20250514", currentFastMode: false);
        result1.BreakDetected.Should().BeFalse("same fast mode, no break");

        var usage2 = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 100
        };
        var result2 = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", usage2, currentModelId: "claude-sonnet-4-20250514", currentFastMode: true);
        result2.BreakDetected.Should().BeTrue("fast mode toggle breaks cache");
        result2.Kind.Should().Be(CacheBreakKind.FastModeChanged);
    }

    [Fact]
    public void MultiTurn_DynamicContentChange_BreaksCache()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System", tools, []);

        var snapshot = _detector.RecordPromptState(prefix, "dynamic v1");

        var usage1 = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 80,
            CacheCreationInputTokens = 0
        };
        var result1 = _detector.CheckCacheBreak(snapshot, prefix, "dynamic v1", usage1);
        result1.BreakDetected.Should().BeFalse("same dynamic content, no break");

        var usage2 = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 100
        };
        var result2 = _detector.CheckCacheBreak(snapshot, prefix, "dynamic v2", usage2);
        result2.BreakDetected.Should().BeTrue("dynamic content change breaks cache");
        result2.Kind.Should().Be(CacheBreakKind.DynamicContentChanged);
    }
}
