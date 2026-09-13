namespace Integration.Tests.PrefixCache.Unit;

public sealed class CacheBreakDetectorDriftIntegrationTests
{
    private readonly CacheBreakDetector _detector = new();

    [Fact]
    public void CheckCacheBreak_ToolAppend_CacheHit_NoBreak_WithDriftReport()
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

        result.BreakDetected.Should().BeFalse();
        result.ToolDrift.Should().NotBeNull();
        result.ToolDrift!.Kind.Should().Be(ToolDriftKind.Append);
        result.ToolDrift.IsCacheSafe.Should().BeTrue();
    }

    [Fact]
    public void CheckCacheBreak_ToolEdit_ReportsBreak_WithDriftReport()
    {
        var toolsBefore = new List<ToolSpec> { new("read", "Read files v1") };
        var toolsAfter = new List<ToolSpec> { new("read", "Read files v2") };

        var prefixBefore = new ImmutablePrefix("System", toolsBefore, []);
        var prefixAfter = new ImmutablePrefix("System", toolsAfter, []);

        var snapshot = _detector.RecordPromptState(prefixBefore, "dynamic");
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };

        var result = _detector.CheckCacheBreak(snapshot, prefixAfter, "dynamic", usage);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.ToolSpecsChanged);
        result.ToolDrift.Should().NotBeNull();
        result.ToolDrift!.Kind.Should().Be(ToolDriftKind.Edit);
        result.ToolDrift.IsCacheSafe.Should().BeFalse();
    }

    [Fact]
    public void CheckCacheBreak_ToolRemove_ReportsBreak_WithDriftReport()
    {
        var toolsBefore = new List<ToolSpec>
        {
            new("read", "Read files"),
            new("write", "Write files")
        };
        var toolsAfter = new List<ToolSpec> { new("read", "Read files") };

        var prefixBefore = new ImmutablePrefix("System", toolsBefore, []);
        var prefixAfter = new ImmutablePrefix("System", toolsAfter, []);

        var snapshot = _detector.RecordPromptState(prefixBefore, "dynamic");
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };

        var result = _detector.CheckCacheBreak(snapshot, prefixAfter, "dynamic", usage);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.ToolSpecsChanged);
        result.ToolDrift.Should().NotBeNull();
        result.ToolDrift!.Kind.Should().Be(ToolDriftKind.Remove);
    }

    [Fact]
    public void CheckCacheBreak_ToolReorder_NoBreak_NoDriftReport()
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

        result.BreakDetected.Should().BeFalse("reorder with stable sorting should not break");
        result.ToolDrift.Should().BeNull("stable sorting makes reorder invisible to hash comparison");
    }

    [Fact]
    public void CheckCacheBreak_NoToolChange_NoDriftReport()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System", tools, []);

        var snapshot = _detector.RecordPromptState(prefix, "dynamic");
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 80, CacheCreationInputTokens = 20 };

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", usage);

        result.BreakDetected.Should().BeFalse();
        result.ToolDrift.Should().BeNull("identical tools should not produce drift report");
    }

    [Fact]
    public void CheckCacheBreak_SystemPromptChange_NoDriftReport()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix1 = new ImmutablePrefix("System v1", tools, []);
        var prefix2 = new ImmutablePrefix("System v2", tools, []);

        var snapshot = _detector.RecordPromptState(prefix1, "dynamic");
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };

        var result = _detector.CheckCacheBreak(snapshot, prefix2, "dynamic", usage);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.SystemPromptChanged);
        result.ToolDrift.Should().BeNull("system prompt change should not produce tool drift report");
    }
}
