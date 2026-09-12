namespace MockServer.Core.Tests;

/// <summary>
/// 真实多轮对话缓存命中验证测试
///
/// 与 PrefixCacheSimulatorTests 不同, 本测试模拟真实多轮对话:
/// - system prompt 保持不变
/// - messages 数组逐步增长 (user1 → user1+assistant1+user2 → ...)
///
/// 旧实现使用 ExtractSystemPrefix 只提取 system, 无法区分多轮对话:
/// - 所有轮次 system 相同 → 全部判定"完全命中" (错误!)
///
/// 修复后使用 ExtractConversationPrefix 提取完整对话前缀:
/// - Turn 1: 完整 miss
/// - Turn 2: 部分命中 (Turn 1 部分已缓存)
/// - Turn 3: 部分命中 (Turn 1+2 部分已缓存, 命中比例更大)
/// </summary>
public sealed class MultiTurnCacheHitTests
{
    /// <summary>
    /// 构造真实多轮对话请求 (system 不变, messages 增长)
    /// </summary>
    private static JsonElement MakeMultiTurnRequest(int turn)
    {
        var messages = new StringBuilder();
        messages.Append("""{"system":"You are a helpful assistant.","messages":[""");
        for (var i = 1; i <= turn; i++)
        {
            if (i > 1) messages.Append(',');
            messages.Append($$"""{"role":"user","content":"question{{i}} with enough content to make it meaningful"}""");
            messages.Append(',');
            messages.Append($$"""{"role":"assistant","content":"answer{{i}} with enough content to make it meaningful"}""");
        }
        messages.Append("]}");

        return JsonDocument.Parse(messages.ToString()).RootElement.Clone();
    }

    [Fact]
    public void MultiTurn_FirstTurn_AlwaysCacheMiss()
    {
        var sim = new PrefixCacheSimulator(
            TokenEstimator.ExtractConversationPrefix,
            TokenEstimator.EstimateFromMessages);

        var req1 = MakeMultiTurnRequest(1);
        var stats1 = sim.ComputeCacheStats(req1);

        stats1.CacheReadTokens.Should().Be(0, "第一轮对话无历史, 应完全 miss");
        stats1.CacheCreationTokens.Should().Be(stats1.InputTokens);
    }

    [Fact]
    public void MultiTurn_SecondTurn_PartialCacheHit()
    {
        var sim = new PrefixCacheSimulator(
            TokenEstimator.ExtractConversationPrefix,
            TokenEstimator.EstimateFromMessages);

        var req1 = MakeMultiTurnRequest(1);
        var req2 = MakeMultiTurnRequest(2);

        sim.ComputeCacheStats(req1);
        var stats2 = sim.ComputeCacheStats(req2);

        // 第二轮: system+user1+assistant1 已缓存, user2 是新增 → 部分命中
        stats2.CacheReadTokens.Should().BeGreaterThan(0, "第二轮应部分命中 (第一轮内容已缓存)");
        stats2.CacheCreationTokens.Should().BeGreaterThan(0, "第二轮有新增内容 (user2), 应有 cache creation");
        stats2.CacheReadTokens.Should().BeLessThan(stats2.InputTokens, "不应完全命中 (有新增内容)");
    }

    [Fact]
    public void MultiTurn_ThirdTurn_HitMoreThanSecondTurn()
    {
        var sim = new PrefixCacheSimulator(
            TokenEstimator.ExtractConversationPrefix,
            TokenEstimator.EstimateFromMessages);

        var req1 = MakeMultiTurnRequest(1);
        var req2 = MakeMultiTurnRequest(2);
        var req3 = MakeMultiTurnRequest(3);

        sim.ComputeCacheStats(req1);
        var stats2 = sim.ComputeCacheStats(req2);
        var stats3 = sim.ComputeCacheStats(req3);

        // 第三轮比第二轮命中更多 (已缓存 prefix 更长)
        stats3.CacheReadTokens.Should().BeGreaterThan(stats2.CacheReadTokens,
            "第三轮已缓存 prefix 更长, 命中 tokens 应多于第二轮");
        stats3.CacheCreationTokens.Should().BeGreaterThan(0, "第三轮有新增内容 (user3), 应有 cache creation");
    }

    [Fact]
    public void MultiTurn_SameTurnRepeated_FullCacheHit()
    {
        var sim = new PrefixCacheSimulator(
            TokenEstimator.ExtractConversationPrefix,
            TokenEstimator.EstimateFromMessages);

        var req = MakeMultiTurnRequest(2);

        sim.ComputeCacheStats(req);
        var stats2 = sim.ComputeCacheStats(req);

        // 完全相同的请求 → 完全命中
        stats2.CacheReadTokens.Should().Be(stats2.InputTokens, "完全相同请求应完全命中");
        stats2.CacheCreationTokens.Should().Be(0);
    }

    [Fact]
    public void MultiTurn_DifferentSystemPrompt_AlwaysCacheMiss()
    {
        var sim = new PrefixCacheSimulator(
            TokenEstimator.ExtractConversationPrefix,
            TokenEstimator.EstimateFromMessages);

        var json1 = """{"system":"System A","messages":[{"role":"user","content":"hello"}]}""";
        var json2 = """{"system":"System B","messages":[{"role":"user","content":"hello"}]}""";

        var req1 = JsonDocument.Parse(json1).RootElement.Clone();
        var req2 = JsonDocument.Parse(json2).RootElement.Clone();

        sim.ComputeCacheStats(req1);
        var stats2 = sim.ComputeCacheStats(req2);

        // 不同 system → 前缀不同 → 完全 miss
        stats2.CacheReadTokens.Should().Be(0, "不同 system prompt 应完全 miss");
        stats2.CacheCreationTokens.Should().Be(stats2.InputTokens);
    }
}
