namespace Core.Context;

/// <summary>
/// DTO 计算属性单元测试 — 覆盖 Stryker 算术与条件变异
/// </summary>
public sealed class DtoComputedPropertyTests
{
    [Fact]
    public void ContextCollapseResult_CompressionRatio_WhenOriginalPositive_ReturnsRatio()
    {
        var result = new ContextCollapseResult
        {
            Collapsed = true,
            CollapsedContent = "collapsed",
            OriginalTokenCount = 100,
            CollapsedTokenCount = 50,
            SegmentsCollapsed = 1,
            SegmentsPreserved = 0,
            Strategy = CollapseStrategy.Balanced
        };

        result.CompressionRatio.Should().Be(0.5);
        result.SavedTokens.Should().Be(50);
    }

    [Fact]
    public void ContextCollapseResult_CompressionRatio_WhenOriginalZero_ReturnsOne()
    {
        var result = new ContextCollapseResult
        {
            Collapsed = false,
            CollapsedContent = string.Empty,
            OriginalTokenCount = 0,
            CollapsedTokenCount = 0,
            SegmentsCollapsed = 0,
            SegmentsPreserved = 0,
            Strategy = CollapseStrategy.Balanced
        };

        result.CompressionRatio.Should().Be(1.0);
        result.SavedTokens.Should().Be(0);
    }

    [Fact]
    public void CompactResult_TokenSavingsRatio_WhenPreCompactPositive_ReturnsRatio()
    {
        var result = new CompactResult
        {
            Compacted = true,
            Level = CompactLevel.FullCompact,
            Trigger = CompactTrigger.Auto,
            PreCompactTokenCount = 1000,
            PostCompactTokenCount = 600
        };

        result.TokenSavingsRatio.Should().Be(0.4);
    }

    [Fact]
    public void CompactResult_TokenSavingsRatio_WhenPreCompactZero_ReturnsZero()
    {
        var result = new CompactResult
        {
            Compacted = false,
            Level = CompactLevel.None,
            Trigger = CompactTrigger.Manual,
            PreCompactTokenCount = 0,
            PostCompactTokenCount = 0
        };

        result.TokenSavingsRatio.Should().Be(0);
    }

    [Fact]
    public void ContextCollapseOptions_Factories_ReturnExpectedDefaults()
    {
        var aggressive = ContextCollapseOptions.Aggressive;
        var balanced = ContextCollapseOptions.Balanced;
        var conservative = ContextCollapseOptions.Conservative;

        aggressive.Strategy.Should().Be(CollapseStrategy.Aggressive);
        aggressive.MinSegmentTokenCount.Should().Be(50);
        aggressive.MinCollapsePriority.Should().Be(0.2);
        aggressive.TargetCompressionRatio.Should().Be(0.3);
        aggressive.MaxSummaryLength.Should().Be(100);

        balanced.Strategy.Should().Be(CollapseStrategy.Balanced);
        balanced.MinSegmentTokenCount.Should().Be(100);

        conservative.Strategy.Should().Be(CollapseStrategy.Conservative);
        conservative.MinSegmentTokenCount.Should().Be(200);
        conservative.MinCollapsePriority.Should().Be(0.5);
        conservative.TargetCompressionRatio.Should().Be(0.7);
        conservative.MaxSummaryLength.Should().Be(300);
    }
}
