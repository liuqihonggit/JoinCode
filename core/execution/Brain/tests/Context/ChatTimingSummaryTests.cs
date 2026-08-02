namespace Core.Tests.Context;

using JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// ChatTiming.FormatSummary 缓存统计输出单元测试
/// 验证 [Timing] 行在传入 TokenUsage 时正确显示前缀缓存命中情况（省钱刚需可见性）
/// </summary>
public partial class ChatTimingSummaryTests
{
    [Fact]
    public void FormatSummary_WithoutUsage_DoesNotContainCacheStats()
    {
        var timing = new ChatTiming();
        timing.StartTotal();
        timing.StopTotal();

        var summary = timing.FormatSummary();

        summary.Should().StartWith("[Timing]");
        summary.Should().Contain("总耗时=");
        summary.Should().NotContain("缓存=");
    }

    [Fact]
    public void FormatSummary_WithNullUsage_DoesNotContainCacheStats()
    {
        var timing = new ChatTiming();
        timing.StartTotal();
        timing.StopTotal();

        var summary = timing.FormatSummary(usage: null);

        summary.Should().StartWith("[Timing]");
        summary.Should().NotContain("缓存=");
    }

    [Fact]
    public void FormatSummary_WithCacheHit_ShowsCacheStats()
    {
        var timing = new ChatTiming();
        timing.StartTotal();
        timing.StopTotal();
        var usage = new TokenUsage
        {
            PromptTokens = 10470,
            CompletionTokens = 100,
            CacheCreationInputTokens = 1729,
            CacheReadInputTokens = 8741,
        };

        var summary = timing.FormatSummary(usage);

        summary.Should().StartWith("[Timing]");
        summary.Should().Contain("缓存=创建1729,读取8741");
    }

    [Fact]
    public void FormatSummary_WithZeroCache_ShowsZeroStats()
    {
        var timing = new ChatTiming();
        timing.StartTotal();
        timing.StopTotal();
        var usage = new TokenUsage
        {
            PromptTokens = 8963,
            CompletionTokens = 50,
            CacheCreationInputTokens = 8963,
            CacheReadInputTokens = 0,
        };

        var summary = timing.FormatSummary(usage);

        summary.Should().StartWith("[Timing]");
        summary.Should().Contain("缓存=创建8963,读取0");
    }

    [Fact]
    public void FormatSummary_PreservesTimingFields_WhenUsageProvided()
    {
        var timing = new ChatTiming();
        timing.StartTotal();
        timing.StopTotal();
        timing.FirstTokenLatencyMs = 563;
        timing.LlmCallCount = 9;
        var usage = new TokenUsage
        {
            CacheCreationInputTokens = 0,
            CacheReadInputTokens = 8741,
        };

        var summary = timing.FormatSummary(usage);

        summary.Should().Contain("首token=563ms");
        summary.Should().Contain("调用9次");
        summary.Should().Contain("缓存=创建0,读取8741");
    }
}
