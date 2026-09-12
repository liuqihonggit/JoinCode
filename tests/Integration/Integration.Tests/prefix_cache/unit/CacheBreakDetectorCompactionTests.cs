namespace JoinCode.Abstractions.LLM.Chat;

public sealed class CacheBreakDetectorCompactionTests
{
    private readonly CacheBreakDetector _detector = new();

    private static TokenUsage Hit(int read, int creation = 0) => new(100, 50)
    {
        CacheReadInputTokens = read,
        CacheCreationInputTokens = creation
    };

    private static TokenUsage Miss() => new(100, 50)
    {
        CacheReadInputTokens = 0,
        CacheCreationInputTokens = 100
    };

    [Fact]
    public void AfterCompaction_NextMiss_ReportedAsCompactionEntered_InsteadOfEviction()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System", tools, []);

        var snapshot1 = _detector.RecordPromptState(prefix, "dynamic");
        var result1 = _detector.CheckCacheBreak(snapshot1, prefix, "dynamic", Hit(0, 80));
        result1.BreakDetected.Should().BeFalse();

        // 折叠改写对话，通知检测器缓存被重写（非驱逐）
        _detector.NotifyCompaction();

        var snapshot2 = _detector.RecordPromptState(prefix, "dynamic");
        var result2 = _detector.CheckCacheBreak(snapshot2, prefix, "dynamic", Miss());

        result2.BreakDetected.Should().BeTrue();
        result2.Kind.Should().Be(CacheBreakKind.CompactionEntered);
    }

    [Fact]
    public void RebuildTurn_AfterCompactionEntered_NotReportedAsEvictionAgain()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System", tools, []);

        var snapshot1 = _detector.RecordPromptState(prefix, "dynamic");
        _detector.CheckCacheBreak(snapshot1, prefix, "dynamic", Hit(0, 80));

        _detector.NotifyCompaction();
        var snapshot2 = _detector.RecordPromptState(prefix, "dynamic");
        var result2 = _detector.CheckCacheBreak(snapshot2, prefix, "dynamic", Miss());
        result2.Kind.Should().Be(CacheBreakKind.CompactionEntered);

        // 压缩后的重建轮：基线已重置，不应当作驱逐或再次误报
        var snapshot3 = _detector.RecordPromptState(prefix, "dynamic");
        var result3 = _detector.CheckCacheBreak(snapshot3, prefix, "dynamic", Miss());
        result3.BreakDetected.Should().BeFalse("baseline reset; rebuild miss should not re-trigger eviction");
    }

    [Fact]
    public void WithoutCompaction_IdenticalMiss_StillReportedAsEviction()
    {
        var tools = new List<ToolSpec> { new("read", "Read files") };
        var prefix = new ImmutablePrefix("System", tools, []);

        var snapshot1 = _detector.RecordPromptState(prefix, "dynamic");
        _detector.CheckCacheBreak(snapshot1, prefix, "dynamic", Hit(10000, 0));

        var snapshot2 = _detector.RecordPromptState(prefix, "dynamic");
        var result2 = _detector.CheckCacheBreak(snapshot2, prefix, "dynamic", Miss());
        result2.Kind.Should().Be(CacheBreakKind.ServerSideRouting);
    }

    [Fact]
    public void SessionStats_RecordsCompactionEntered_SeparatelyFromEviction()
    {
        var stats = new SessionStats();
        stats.RecordTurn(new TokenUsage(100, 50), 0, CacheBreakResult.Break(CacheBreakKind.CompactionEntered, "compacted"));
        stats.RecordTurn(new TokenUsage(100, 50), 0, CacheBreakResult.Break(CacheBreakKind.CacheEviction, "evicted"));

        stats.CacheEvictionBreaks.Should().Be(1);
        stats.CompactionEnteredBreaks.Should().Be(1);
    }
}