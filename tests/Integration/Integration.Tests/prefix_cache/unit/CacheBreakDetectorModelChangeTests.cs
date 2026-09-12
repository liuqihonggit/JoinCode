namespace Integration.Tests.PrefixCache.Unit;

public sealed class CacheBreakDetectorModelChangeTests
{
    private readonly CacheBreakDetector _detector = new();

    [Fact]
    public void CheckCacheBreak_ModelChanged_ShouldDetectBreak()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System prompt", tools, []);

        var snapshot = _detector.RecordPromptState(prefix, "dynamic", modelId: "claude-sonnet-4-20250514");

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 100
        };

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", usage, currentModelId: "claude-haiku-4-20250514");

        result.BreakDetected.Should().BeTrue(
            "model change should be detected as a cache break — " +
            "Anthropic caches are per-model, switching models invalidates the cache");
        result.Kind.Should().Be(CacheBreakKind.ModelChanged);
    }

    [Fact]
    public void CheckCacheBreak_SameModel_NoBreak()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System prompt", tools, []);

        var snapshot = _detector.RecordPromptState(prefix, "dynamic", modelId: "claude-sonnet-4-20250514");

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 80,
            CacheCreationInputTokens = 0
        };

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", usage, currentModelId: "claude-sonnet-4-20250514");

        result.BreakDetected.Should().BeFalse(
            "same model should not trigger ModelChanged break");
    }

    [Fact]
    public void CheckCacheBreak_ModelChanged_DetectedBeforeOtherChanges()
    {
        var toolsBefore = new List<ToolSpec> { new("read", "Read files") };
        var toolsAfter = new List<ToolSpec> { new("read", "Read files v2") };
        var prefixBefore = new ImmutablePrefix("System", toolsBefore, []);
        var prefixAfter = new ImmutablePrefix("System", toolsAfter, []);

        var snapshot = _detector.RecordPromptState(prefixBefore, "dynamic", modelId: "claude-sonnet-4-20250514");

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 100
        };

        var result = _detector.CheckCacheBreak(snapshot, prefixAfter, "dynamic", usage, currentModelId: "claude-haiku-4-20250514");

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.ModelChanged,
            "model change should be detected before tool specs change — " +
            "model change is the primary cause of cache invalidation");
    }

    [Fact]
    public void CheckCacheBreak_NullModel_NoModelDetection()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System prompt", tools, []);

        var snapshot = _detector.RecordPromptState(prefix, "dynamic", modelId: null);

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 80,
            CacheCreationInputTokens = 0
        };

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", usage, currentModelId: null);

        result.BreakDetected.Should().BeFalse(
            "null model should not trigger ModelChanged");
    }

    [Fact]
    public void CheckCacheBreak_FastModeChanged_ShouldDetectBreak()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System prompt", tools, []);

        var snapshot = _detector.RecordPromptState(prefix, "dynamic", modelId: "claude-sonnet-4-20250514", fastMode: false);

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 100
        };

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", usage, currentModelId: "claude-sonnet-4-20250514", currentFastMode: true);

        result.BreakDetected.Should().BeTrue(
            "fast mode toggle changes the underlying model, which invalidates the cache");
        result.Kind.Should().Be(CacheBreakKind.FastModeChanged);
    }
}
