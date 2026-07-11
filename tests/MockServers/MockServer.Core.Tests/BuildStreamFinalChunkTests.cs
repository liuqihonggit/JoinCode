namespace MockServer.Core.Tests;

using Anthropic.MockServer;
using DeepSeek.MockServer;
using OpenAI.MockServer;

/// <summary>
/// BuildStreamFinalChunk 流式最终 chunk cache stats 测试
///
/// 背景: 真实 LLM 流式响应的最后一个 chunk 包含 usage/cache stats:
/// - OpenAI: stream_options.include_usage=true 时, 最后一个 chunk 包含 usage 字段
/// - Anthropic: message_delta 事件包含 cache_creation_input_tokens/cache_read_input_tokens
/// - DeepSeek: 最后一个 chunk 的 usage 包含 prompt_cache_hit_tokens/prompt_cache_miss_tokens
///
/// 旧实现: 流式响应调用 BuildStreamChunk(id, "", true) 作为最终 chunk, 不包含 cache stats
/// 修复目标: 新增 BuildStreamFinalChunk(id, cacheStats) 方法, 返回包含 cache stats 的最终 chunk
///
/// 注意: BuildStreamFinalChunk 是 IResponseStrategy 的默认接口方法 (DIM)。
/// 测试中通过 IResponseStrategy 引用调用, 派生类 override 后即可直接通过类引用调用。
/// </summary>
public sealed class BuildStreamFinalChunkTests
{
    private static readonly CacheStats MissStats = new()
    {
        CacheCreationTokens = 100,
        CacheReadTokens = 0,
        InputTokens = 100,
        OutputTokens = 50
    };

    private static readonly CacheStats HitStats = new()
    {
        CacheCreationTokens = 0,
        CacheReadTokens = 100,
        InputTokens = 100,
        OutputTokens = 50
    };

    // ============== OpenAI ==============

    [Fact]
    public void OpenAI_BuildStreamFinalChunk_IncludesUsageAndCachedTokens()
    {
        IResponseStrategy strategy = new OpenAIResponseStrategy(turns: null, defaultResponse: "test");
        var id = "chatcmpl-test123";

        var chunk = strategy.BuildStreamFinalChunk(id, HitStats);

        // 必须包含 usage 字段
        chunk.Should().Contain("\"usage\"");
        // 必须包含 prompt_tokens_details.cached_tokens (OpenAI cache read 字段)
        chunk.Should().Contain("cached_tokens");
        // hit 时 cached_tokens=100 (JSON 数字, 无引号)
        chunk.Should().Contain("\"cached_tokens\":100");
        // 必须以 [DONE] 结尾
        chunk.Should().Contain("[DONE]");
        // 必须包含 finish_reason: stop
        chunk.Should().Contain("\"finish_reason\":\"stop\"");
    }

    [Fact]
    public void OpenAI_BuildStreamFinalChunk_OnCacheMiss_HasZeroCachedTokens()
    {
        IResponseStrategy strategy = new OpenAIResponseStrategy(turns: null, defaultResponse: "test");

        var chunk = strategy.BuildStreamFinalChunk("id-miss", MissStats);

        // miss 时 cached_tokens 应为 0
        chunk.Should().Contain("\"cached_tokens\":0");
        chunk.Should().Contain("\"prompt_tokens\":100");
        chunk.Should().Contain("\"completion_tokens\":50");
    }

    // ============== Anthropic ==============

    [Fact]
    public void Anthropic_BuildStreamFinalChunk_IncludesCacheStatsInMessageDelta()
    {
        IResponseStrategy strategy = new AnthropicResponseStrategy(turns: null, defaultResponse: "test");
        var id = "msg-test123";

        var chunk = strategy.BuildStreamFinalChunk(id, HitStats);

        // 必须包含 message_delta 事件
        chunk.Should().Contain("message_delta");
        // 必须包含 cache_creation_input_tokens 和 cache_read_input_tokens
        chunk.Should().Contain("cache_creation_input_tokens");
        chunk.Should().Contain("cache_read_input_tokens");
        // hit 时 cache_read_input_tokens 应为 100
        chunk.Should().Contain("\"cache_read_input_tokens\":100");
        // 必须以 message_stop 事件结尾
        chunk.Should().Contain("message_stop");
    }

    [Fact]
    public void Anthropic_BuildStreamFinalChunk_OnCacheMiss_HasCacheCreationTokens()
    {
        IResponseStrategy strategy = new AnthropicResponseStrategy(turns: null, defaultResponse: "test");

        var chunk = strategy.BuildStreamFinalChunk("id-miss", MissStats);

        // miss 时 cache_creation_input_tokens=100, cache_read_input_tokens=0
        chunk.Should().Contain("\"cache_creation_input_tokens\":100");
        chunk.Should().Contain("\"cache_read_input_tokens\":0");
    }

    // ============== DeepSeek ==============

    [Fact]
    public void DeepSeek_BuildStreamFinalChunk_IncludesPromptCacheHitMissTokens()
    {
        IResponseStrategy strategy = new DeepSeekResponseStrategy(turns: null, defaultResponse: "test");
        var id = "chatcmpl-test123";

        var chunk = strategy.BuildStreamFinalChunk(id, HitStats);

        // 必须包含 usage 字段
        chunk.Should().Contain("\"usage\"");
        // 必须包含 prompt_cache_hit_tokens 和 prompt_cache_miss_tokens (DeepSeek 特有字段)
        chunk.Should().Contain("prompt_cache_hit_tokens");
        chunk.Should().Contain("prompt_cache_miss_tokens");
        // hit 时 prompt_cache_hit_tokens=100, prompt_cache_miss_tokens=0
        chunk.Should().Contain("\"prompt_cache_hit_tokens\":100");
        chunk.Should().Contain("\"prompt_cache_miss_tokens\":0");
        // 必须以 [DONE] 结尾
        chunk.Should().Contain("[DONE]");
    }

    [Fact]
    public void DeepSeek_BuildStreamFinalChunk_OnCacheMiss_HasCacheMissTokens()
    {
        IResponseStrategy strategy = new DeepSeekResponseStrategy(turns: null, defaultResponse: "test");

        var chunk = strategy.BuildStreamFinalChunk("id-miss", MissStats);

        // miss 时 prompt_cache_hit_tokens=0, prompt_cache_miss_tokens=100
        chunk.Should().Contain("\"prompt_cache_hit_tokens\":0");
        chunk.Should().Contain("\"prompt_cache_miss_tokens\":100");
    }

    // ============== Default interface impl ==============

    [Fact]
    public void DefaultBuildStreamFinalChunk_FallsBackToBuildStreamChunkLast()
    {
        // 默认接口实现回退到 BuildStreamChunk(id, "", true)
        // 用于验证向后兼容: 未实现 BuildStreamFinalChunk 的策略仍能工作
        IResponseStrategy strategy = new DefaultImplStrategy();
        var chunk = strategy.BuildStreamFinalChunk("id-test", HitStats);

        // 默认实现不包含 usage 字段 (回退到普通最终 chunk)
        chunk.Should().Be("default-last-chunk");
    }

    private sealed class DefaultImplStrategy : IResponseStrategy
    {
        public string BuildResponse(JsonElement request, CacheStats cacheStats) => "";
        public bool SupportsStreaming => true;
        public string BuildStreamChunk(string id, string content, bool isLast)
            => isLast ? "default-last-chunk" : "default-chunk";
        public string BuildToolCallResponse(JsonElement request, CacheStats cacheStats) => "";
        public string BuildStreamToolCallResponse(string id) => "";
    }
}
