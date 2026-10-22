namespace NativeTui.Tests.Repl;

/// <summary>
/// TokenUsageTracker 测试 — 契约 API（RecordUsage 累计/快照容忍）+ 三态警告阈值
/// （对齐基准 autoCompact.ts:62-65 缓冲常量与 TokenWarning.tsx:166-177 分支）：
/// limit=200k 下显示阈值 167k（=200k-13k-20k）、自动压缩线 187k（=200k-13k）；
/// percentLeft = (limit-context)/limit（max(0) 钳制 + AwayFromZero 舍入）；
/// 无 limit（null/≤0）恒 None；null 快照保持原值（引擎部分上报容忍）。
/// </summary>
public sealed class TokenUsageTrackerTests
{
    /// <summary>测试用窗口上限（200k — 阈值派生清晰）。</summary>
    private const long Limit = 200_000;

    /// <summary>构造已上报一次用量的 tracker（context/limit 快照注入）。</summary>
    private static TokenUsageTracker Tracker(long context, long limit = Limit)
    {
        var tracker = new TokenUsageTracker();
        tracker.RecordUsage(0, 0, context, limit);
        return tracker;
    }

    [Fact]
    public void RecordUsage_AccumulatesInputOutput()
    {
        var tracker = new TokenUsageTracker();
        tracker.RecordUsage(100, 50, 1000, Limit);
        tracker.RecordUsage(200, 80, 1300, null);

        Assert.Equal(300, tracker.InputTokens);
        Assert.Equal(130, tracker.OutputTokens);
        Assert.Equal(430, tracker.TotalTokens);
        Assert.Equal(1300, tracker.ContextTokens);
    }

    [Fact]
    public void RecordUsage_NullSnapshots_KeepPrevious()
    {
        var tracker = new TokenUsageTracker();
        tracker.RecordUsage(10, 5, 500, Limit);

        tracker.RecordUsage(1, 1, null, null);

        Assert.Equal(500, tracker.ContextTokens);
        Assert.Equal(Limit, tracker.ContextLimitTokens);
    }

    [Fact]
    public void Warning_WithoutLimit_IsNone()
    {
        Assert.Equal(TokenWarningLevel.None, Tracker(150_000, limit: 0).Warning.Level);
        var unset = Tracker(150_000, limit: 0);
        unset.Reset();
        unset.RecordUsage(10, 5, 150_000, null);
        Assert.Equal(TokenWarningLevel.None, unset.Warning.Level);
        Assert.Null(unset.Warning.Text);
    }

    [Fact]
    public void Warning_BelowDisplayThreshold_IsNone()
    {
        var warning = Tracker(166_999).Warning;

        Assert.Equal(TokenWarningLevel.None, warning.Level);
        Assert.Null(warning.Text);
    }

    [Fact]
    public void Warning_AtDisplayThreshold_IsOkWithPercentText()
    {
        // 167k = 显示阈值边界：percentLeft = 33000/200000 = 16.5 → AwayFromZero → 17
        var warning = Tracker(167_000).Warning;

        Assert.Equal(TokenWarningLevel.Ok, warning.Level);
        Assert.Equal(17, warning.PercentLeft);
        Assert.Equal("17% 直到自动压缩", warning.Text);
    }

    [Fact]
    public void Warning_MidBand_IsOk()
    {
        // 170k（Ok 带中段 [167k,187k)）：percentLeft = 30000/200000 = 15
        var warning = Tracker(170_000).Warning;

        Assert.Equal(TokenWarningLevel.Ok, warning.Level);
        Assert.Equal("15% 直到自动压缩", warning.Text);
    }

    [Fact]
    public void Warning_JustBelowAutoCompactLine_IsOk()
    {
        var warning = Tracker(186_999).Warning;

        Assert.Equal(TokenWarningLevel.Ok, warning.Level);
    }

    [Fact]
    public void Warning_AtAutoCompactLine_IsLow()
    {
        // 187k = limit-13k 自动压缩线：percentLeft = 13000/200000 = 6.5 → 7
        var warning = Tracker(187_000).Warning;

        Assert.Equal(TokenWarningLevel.Low, warning.Level);
        Assert.Equal(7, warning.PercentLeft);
        Assert.Equal("上下文不足 (7%) · 运行 /compact", warning.Text);
    }

    [Fact]
    public void Warning_AtLimit_IsLowWithZeroPercent()
    {
        var warning = Tracker(Limit).Warning;

        Assert.Equal(TokenWarningLevel.Low, warning.Level);
        Assert.Equal(0, warning.PercentLeft);
        Assert.Equal("上下文不足 (0%) · 运行 /compact", warning.Text);
    }

    [Fact]
    public void Warning_OverLimit_ClampsToZero()
    {
        var warning = Tracker(250_000).Warning;

        Assert.Equal(0, warning.PercentLeft);
        Assert.Equal(TokenWarningLevel.Low, warning.Level);
    }

    [Fact]
    public void Reset_ClearsAll()
    {
        var tracker = Tracker(190_000);
        tracker.RecordUsage(100, 50, 190_000, Limit);

        tracker.Reset();

        Assert.Equal(0, tracker.InputTokens);
        Assert.Equal(0, tracker.OutputTokens);
        Assert.Null(tracker.ContextTokens);
        Assert.Null(tracker.ContextLimitTokens);
        Assert.Equal(TokenWarningLevel.None, tracker.Warning.Level);
    }
}
