namespace MockServer.Core.Tests;

/// <summary>
/// PrefixCacheSimulator 前缀匹配语义测试
///
/// 背景: 真实 LLM Prefix Cache 基于"前缀匹配"——
/// - 1st 请求: 完整 miss, 将 prefix 加入缓存
/// - 2nd 请求: 若新 prefix 以已缓存 prefix 为前缀(或反之), 部分命中
/// - 同 prefix 再次请求: 完全命中 (cache_read = input_tokens, cache_creation = 0)
///
/// 旧实现使用 _seenPrefixes.Contains(prefix) 完整字符串相等匹配, 无法模拟:
/// - 多轮对话中 prefix 增长导致的"部分命中"场景
/// - 真实 LLM 的 prefix cache 语义
///
/// 修复目标:
/// 1. 完全相同 prefix → 完全命中 (cacheRead=inputTokens, cacheCreation=0)
/// 2. 新 prefix 以已缓存 prefix 为前缀 → 部分命中 (按长度比例计算 cacheRead/cacheCreation)
/// 3. 已缓存 prefix 以新 prefix 为前缀 → 完全命中 (新请求完全在已缓存范围内)
/// 4. 完全无交集 → 完全 miss
/// </summary>
public sealed class PrefixCacheSimulatorTests
{
    private static JsonElement MakeRequest(string systemPrompt, int messageChars = 100)
    {
        var json = $$"""{"system":"{{systemPrompt}}","messages":[{"role":"user","content":"{{new string('x', messageChars)}}"}]}""";
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static JsonElement MakeRequestWithPrefix(string prefix, int totalTokens = 100)
    {
        // 简单构造: prefix 直接作为 system, tokens 通过 messages 长度模拟
        var json = $$"""{"system":"{{prefix}}","messages":[{"role":"user","content":"{{new string('x', totalTokens * 4)}}"}]}""";
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static PrefixCacheSimulator CreateSimulator()
        => new(TokenEstimator.ExtractSystemPrefix, TokenEstimator.EstimateFromMessages);

    [Fact]
    public void FirstCall_IsAlwaysCacheMiss()
    {
        var sim = CreateSimulator();
        var req = MakeRequest("system prompt A", 100);

        var stats = sim.ComputeCacheStats(req);

        stats.CacheReadTokens.Should().Be(0);
        stats.CacheCreationTokens.Should().Be(stats.InputTokens);
    }

    [Fact]
    public void SamePrefix_SecondCall_FullCacheHit()
    {
        var sim = CreateSimulator();
        var req1 = MakeRequest("system prompt A", 100);
        var req2 = MakeRequest("system prompt A", 100);

        sim.ComputeCacheStats(req1);
        var stats2 = sim.ComputeCacheStats(req2);

        // 完全相同 prefix → 完全命中
        stats2.CacheReadTokens.Should().Be(stats2.InputTokens);
        stats2.CacheCreationTokens.Should().Be(0);
    }

    [Fact]
    public void DifferentSystemPrompt_SecondCall_CacheMiss()
    {
        var sim = CreateSimulator();
        var req1 = MakeRequest("system prompt A", 100);
        var req2 = MakeRequest("system prompt B", 100);

        sim.ComputeCacheStats(req1);
        var stats2 = sim.ComputeCacheStats(req2);

        // 完全不同的 system → 无前缀匹配 → 完全 miss
        stats2.CacheReadTokens.Should().Be(0);
        stats2.CacheCreationTokens.Should().Be(stats2.InputTokens);
    }

    [Fact]
    public void GrowingPrefix_SecondCall_PartialCacheHit()
    {
        // 多轮对话场景: 第2轮 prefix 比第1轮长 (包含第1轮的内容)
        // 但 ExtractSystemPrefix 只提取 system, 所以这个测试用扩展的 system 模拟
        var sim = CreateSimulator();
        var req1 = MakeRequest("base system prompt", 100);
        var req2 = MakeRequest("base system prompt with extension", 100);

        sim.ComputeCacheStats(req1);
        var stats2 = sim.ComputeCacheStats(req2);

        // 第2轮 prefix 以第1轮 prefix 为前缀 → 部分命中
        // 修复后期望: cacheReadTokens > 0 (按 base 长度比例), cacheCreationTokens > 0 (extension 部分)
        stats2.CacheReadTokens.Should().BeGreaterThan(0);
        stats2.CacheCreationTokens.Should().BeGreaterThan(0);
        stats2.CacheReadTokens.Should().BeLessThan(stats2.InputTokens);
    }

    [Fact]
    public void ShrinkingPrefix_SecondCall_FullCacheHit()
    {
        // 新 prefix 比已缓存 prefix 短, 但已缓存 prefix 以新 prefix 为前缀 → 完全命中
        var sim = CreateSimulator();
        var req1 = MakeRequest("long system prompt with extension", 100);
        var req2 = MakeRequest("long system prompt", 100);

        sim.ComputeCacheStats(req1);
        var stats2 = sim.ComputeCacheStats(req2);

        // 新请求完全在已缓存范围内 → 完全命中
        stats2.CacheReadTokens.Should().Be(stats2.InputTokens);
        stats2.CacheCreationTokens.Should().Be(0);
    }

    [Fact]
    public void ThreeTurnProgressiveGrowth_EachTurnHasPartialHit()
    {
        // 三轮对话, prefix 逐步增长
        var sim = CreateSimulator();
        var req1 = MakeRequest("turn1", 100);
        var req2 = MakeRequest("turn1 turn2", 100);
        var req3 = MakeRequest("turn1 turn2 turn3", 100);

        var stats1 = sim.ComputeCacheStats(req1);
        var stats2 = sim.ComputeCacheStats(req2);
        var stats3 = sim.ComputeCacheStats(req3);

        // 第1轮: 完全 miss
        stats1.CacheReadTokens.Should().Be(0);
        stats1.CacheCreationTokens.Should().Be(stats1.InputTokens);

        // 第2轮: 部分命中 (turn1 部分)
        stats2.CacheReadTokens.Should().BeGreaterThan(0);
        stats2.CacheCreationTokens.Should().BeGreaterThan(0);

        // 第3轮: 部分命中 (turn1+turn2 部分, 应该比第2轮命中更多)
        stats3.CacheReadTokens.Should().BeGreaterThan(stats2.CacheReadTokens);
        stats3.CacheCreationTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ResetCache_NextCallIsAlwaysMiss()
    {
        var sim = CreateSimulator();
        var req = MakeRequest("system prompt A", 100);

        sim.ComputeCacheStats(req);
        sim.ResetCache();
        var stats = sim.ComputeCacheStats(req);

        stats.CacheReadTokens.Should().Be(0);
        stats.CacheCreationTokens.Should().Be(stats.InputTokens);
    }
}
