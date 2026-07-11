namespace Integration.Tests.PrefixCache.Unit;

public sealed class CacheBreakDetectorStableSortTests
{
    private readonly CacheBreakDetector _detector = new();

    [Fact]
    public void RecordPromptState_SameToolsDifferentOrder_SameSnapshot()
    {
        var toolsAb = new List<ToolSpec>
        {
            new("alpha", "Alpha tool"),
            new("beta", "Beta tool")
        };
        var toolsBa = new List<ToolSpec>
        {
            new("beta", "Beta tool"),
            new("alpha", "Alpha tool")
        };

        var prefixAb = new ImmutablePrefix("System", toolsAb, []);
        var prefixBa = new ImmutablePrefix("System", toolsBa, []);

        var snapAb = _detector.RecordPromptState(prefixAb, "dynamic");
        var snapBa = _detector.RecordPromptState(prefixBa, "dynamic");

        snapAb.ToolSpecsHash.Should().Be(snapBa.ToolSpecsHash,
            "tool specs hash must be order-independent");
        snapAb.ToolNamesHash.Should().Be(snapBa.ToolNamesHash,
            "tool names hash must be order-independent");
        snapAb.SystemPromptHash.Should().Be(snapBa.SystemPromptHash);
    }

    [Fact]
    public void CheckCacheBreak_SameToolsDifferentOrder_NoBreak()
    {
        var toolsAb = new List<ToolSpec>
        {
            new("alpha", "Alpha tool"),
            new("beta", "Beta tool")
        };
        var toolsBa = new List<ToolSpec>
        {
            new("beta", "Beta tool"),
            new("alpha", "Alpha tool")
        };

        var prefixAb = new ImmutablePrefix("System", toolsAb, []);
        var prefixBa = new ImmutablePrefix("System", toolsBa, []);

        var snapshot = _detector.RecordPromptState(prefixAb, "dynamic");
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 80, CacheCreationInputTokens = 20 };

        var result = _detector.CheckCacheBreak(snapshot, prefixBa, "dynamic", usage);

        result.BreakDetected.Should().BeFalse("tool reorder should not trigger cache break with stable sorting");
    }

    [Fact]
    public void CheckCacheBreak_ToolContentChanged_StillDetectsBreak()
    {
        var tools1 = new List<ToolSpec> { new("read", "Read files v1") };
        var tools2 = new List<ToolSpec> { new("read", "Read files v2") };

        var prefix1 = new ImmutablePrefix("System", tools1, []);
        var prefix2 = new ImmutablePrefix("System", tools2, []);

        var snapshot = _detector.RecordPromptState(prefix1, "dynamic");
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };

        var result = _detector.CheckCacheBreak(snapshot, prefix2, "dynamic", usage);

        result.BreakDetected.Should().BeTrue("tool content change must still be detected");
        result.Kind.Should().Be(CacheBreakKind.ToolSpecsChanged);
    }

    [Fact]
    public void CheckCacheBreak_AppendOnly_NoBreakWhenCacheHit()
    {
        var toolsBefore = new List<ToolSpec> { new("read", "Read files") };
        var toolsAfter = new List<ToolSpec>
        {
            new("read", "Read files"),
            new("write", "Write files")
        };

        var prefixBefore = new ImmutablePrefix("System", toolsBefore, []);
        var prefixAfter = new ImmutablePrefix("System", toolsAfter, []);

        var snapshot = _detector.RecordPromptState(prefixBefore, "dynamic");
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 80, CacheCreationInputTokens = 20 };

        var result = _detector.CheckCacheBreak(snapshot, prefixAfter, "dynamic", usage);

        result.BreakDetected.Should().BeFalse("append-only with cache hit should not break");
    }

    [Fact]
    public void CheckCacheBreak_AppendOnlyButCacheMiss_ReportsBreak()
    {
        var toolsBefore = new List<ToolSpec> { new("read", "Read files") };
        var toolsAfter = new List<ToolSpec>
        {
            new("read", "Read files"),
            new("write", "Write files")
        };

        var prefixBefore = new ImmutablePrefix("System", toolsBefore, []);
        var prefixAfter = new ImmutablePrefix("System", toolsAfter, []);

        var snapshot = _detector.RecordPromptState(prefixBefore, "dynamic");
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };

        var result = _detector.CheckCacheBreak(snapshot, prefixAfter, "dynamic", usage);

        result.BreakDetected.Should().BeTrue("append-only but cache miss should report break");
        result.Kind.Should().Be(CacheBreakKind.ToolSpecsChanged);
    }

    [Fact]
    public void RecordPromptState_ThreeToolsDifferentOrder_SameSnapshot()
    {
        var tools123 = new List<ToolSpec>
        {
            new("read", "Read files"),
            new("write", "Write files"),
            new("search", "Search code")
        };
        var tools321 = new List<ToolSpec>
        {
            new("search", "Search code"),
            new("write", "Write files"),
            new("read", "Read files")
        };

        var prefix123 = new ImmutablePrefix("System", tools123, []);
        var prefix321 = new ImmutablePrefix("System", tools321, []);

        var snap123 = _detector.RecordPromptState(prefix123, "dynamic");
        var snap321 = _detector.RecordPromptState(prefix321, "dynamic");

        snap123.ToolSpecsHash.Should().Be(snap321.ToolSpecsHash);
        snap123.ToolNamesHash.Should().Be(snap321.ToolNamesHash);
    }
}
