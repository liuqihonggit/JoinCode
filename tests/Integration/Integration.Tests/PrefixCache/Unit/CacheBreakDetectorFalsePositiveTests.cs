namespace Integration.Tests.PrefixCache.Unit;

public sealed class CacheBreakDetectorFalsePositiveTests
{
    private readonly CacheBreakDetector _detector = new();

    [Fact]
    public void CheckCacheBreak_FirstRequest_IdenticalState_ShouldNotBeCacheEviction()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System prompt", tools, []);

        var snapshot = _detector.RecordPromptState(prefix, "dynamic");

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 100
        };

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", usage);

        result.BreakDetected.Should().BeFalse(
            "first request with identical state should not be reported as CacheEviction — " +
            "CacheReadInputTokens=0 is expected when no prior cache exists");
    }

    [Fact]
    public void CheckCacheBreak_NonCachingProvider_ToolAppend_ShouldNotBeBreak()
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

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 0
        };

        var result = _detector.CheckCacheBreak(snapshot, prefixAfter, "dynamic", usage);

        result.BreakDetected.Should().BeFalse(
            "non-caching provider (both cache stats = 0) with append-only tool change " +
            "should not report ToolSpecsChanged — CacheReadInputTokens=0 is meaningless " +
            "when the provider doesn't support prefix caching");
    }

    [Fact]
    public void CheckCacheBreak_NonCachingProvider_IdenticalState_NoBreak()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System", tools, []);

        var snapshot = _detector.RecordPromptState(prefix, "dynamic");

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 0
        };

        var result = _detector.CheckCacheBreak(snapshot, prefix, "dynamic", usage);

        result.BreakDetected.Should().BeFalse(
            "non-caching provider with identical state should not report any break");
    }

    [Fact]
    public void CheckCacheBreak_NonCachingProvider_ToolEdit_StillDetectsBreak()
    {
        var toolsBefore = new List<ToolSpec> { new("read", "Read files v1") };
        var toolsAfter = new List<ToolSpec> { new("read", "Read files v2") };

        var prefixBefore = new ImmutablePrefix("System", toolsBefore, []);
        var prefixAfter = new ImmutablePrefix("System", toolsAfter, []);

        var snapshot = _detector.RecordPromptState(prefixBefore, "dynamic");

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 0
        };

        var result = _detector.CheckCacheBreak(snapshot, prefixAfter, "dynamic", usage);

        result.BreakDetected.Should().BeTrue(
            "tool edit (non-cache-safe drift) should still be detected even for non-caching provider");
        result.Kind.Should().Be(CacheBreakKind.ToolSpecsChanged);
    }

    [Fact]
    public void CheckCacheBreak_NonCachingProvider_ToolRemove_StillDetectsBreak()
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

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 0
        };

        var result = _detector.CheckCacheBreak(snapshot, prefixAfter, "dynamic", usage);

        result.BreakDetected.Should().BeTrue(
            "tool remove (non-cache-safe drift) should still be detected even for non-caching provider");
        result.Kind.Should().Be(CacheBreakKind.ToolSpecsChanged);
    }

    [Fact]
    public void CheckCacheBreak_CachingProvider_CacheEviction_StillDetected()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System", tools, []);

        var snapshot1 = _detector.RecordPromptState(prefix, "dynamic");

        var usage1 = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 80,
            CacheCreationInputTokens = 0
        };
        var result1 = _detector.CheckCacheBreak(snapshot1, prefix, "dynamic", usage1);
        result1.BreakDetected.Should().BeFalse("first request with cache hit should not break");

        var snapshot2 = _detector.RecordPromptState(prefix, "dynamic");

        var usage2 = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 100
        };
        var result2 = _detector.CheckCacheBreak(snapshot2, prefix, "dynamic", usage2);

        result2.BreakDetected.Should().BeTrue(
            "second request with identical state but CacheReadInputTokens=0 " +
            "is a genuine cache eviction (prior request had cache hit)");
        result2.Kind.Should().Be(CacheBreakKind.CacheEviction);
    }

    [Fact]
    public void CheckCacheBreak_CachingProvider_ToolAppend_CacheHit_NoBreak()
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

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 80,
            CacheCreationInputTokens = 20
        };

        var result = _detector.CheckCacheBreak(snapshot, prefixAfter, "dynamic", usage);

        result.BreakDetected.Should().BeFalse(
            "caching provider with append-only tool change and cache hit should not break");
    }

    [Fact]
    public void CheckCacheBreak_InputSchemaJsonChanged_ShouldDetectBreak()
    {
        var toolsBefore = new List<ToolSpec> { new("read", "Read files", "{\"type\":\"object\"}") };
        var toolsAfter = new List<ToolSpec> { new("read", "Read files", "{\"type\":\"object\",\"required\":[\"path\"]}") };

        var prefixBefore = new ImmutablePrefix("System", toolsBefore, []);
        var prefixAfter = new ImmutablePrefix("System", toolsAfter, []);

        var snapshot = _detector.RecordPromptState(prefixBefore, "dynamic");

        var usage = new TokenUsage(100, 50)
        {
            CacheReadInputTokens = 0,
            CacheCreationInputTokens = 100
        };

        var result = _detector.CheckCacheBreak(snapshot, prefixAfter, "dynamic", usage);

        result.BreakDetected.Should().BeTrue(
            "InputSchemaJson change must be detected as ToolSpecsChanged — " +
            "tool parameter schema change invalidates prefix cache");
        result.Kind.Should().Be(CacheBreakKind.ToolSpecsChanged);
    }
}
