using Api.LLM.CacheProtocol;

namespace Llm.Tests.Adapters.CacheProtocol;

public sealed class OpenAICacheProtocolTests
{
    private readonly OpenAICacheProtocol _protocol = new();

    [Fact]
    public void RequiresExplicitCacheMarkers_ShouldBeFalse()
    {
        _protocol.RequiresExplicitCacheMarkers.Should().BeFalse(
            "OpenAI uses automatic prefix caching without explicit markers");
    }

    [Fact]
    public void DefaultCacheScope_ShouldBeNull()
    {
        _protocol.DefaultCacheScope.Should().BeNull(
            "OpenAI does not support cache scope concept");
    }

    [Fact]
    public void MapUsage_WithCachedTokens_ShouldMapCorrectly()
    {
        var usage = new OpenAIUsage
        {
            PromptTokens = 100,
            CompletionTokens = 50,
            PromptTokensDetails = new OpenAIPromptTokensDetails { CachedTokens = 80 }
        };

        var result = _protocol.MapUsage(usage);

        result.PromptTokens.Should().Be(100);
        result.CompletionTokens.Should().Be(50);
        result.CacheReadInputTokens.Should().Be(80);
        result.CacheCreationInputTokens.Should().Be(0,
            "OpenAI does not report cache creation tokens via PromptTokensDetails");
    }

    [Fact]
    public void MapUsage_WithoutCacheDetails_ShouldDefaultToZero()
    {
        var usage = new OpenAIUsage
        {
            PromptTokens = 100,
            CompletionTokens = 50
        };

        var result = _protocol.MapUsage(usage);

        result.CacheReadInputTokens.Should().Be(0);
        result.CacheCreationInputTokens.Should().Be(0);
    }

    [Fact]
    public void MapUsage_DeepSeekFormat_PromptCacheHitAndMiss_ShouldMapCorrectly()
    {
        var usage = new OpenAIUsage
        {
            PromptTokens = 100,
            CompletionTokens = 50,
            PromptCacheHitTokens = 70,
            PromptCacheMissTokens = 30
        };

        var result = _protocol.MapUsage(usage);

        result.CacheReadInputTokens.Should().Be(70,
            "DeepSeek prompt_cache_hit_tokens maps to CacheReadInputTokens");
        result.CacheCreationInputTokens.Should().Be(30,
            "DeepSeek prompt_cache_miss_tokens maps to CacheCreationInputTokens");
    }

    [Fact]
    public void MapUsage_DeepSeekFormat_OnlyHitTokens_ShouldMapCorrectly()
    {
        var usage = new OpenAIUsage
        {
            PromptTokens = 100,
            CompletionTokens = 50,
            PromptCacheHitTokens = 80
        };

        var result = _protocol.MapUsage(usage);

        result.CacheReadInputTokens.Should().Be(80);
        result.CacheCreationInputTokens.Should().Be(0);
    }

    [Fact]
    public void MapUsage_DeepSeekFormat_TakesPrecedenceOverPromptTokensDetails()
    {
        var usage = new OpenAIUsage
        {
            PromptTokens = 100,
            CompletionTokens = 50,
            PromptTokensDetails = new OpenAIPromptTokensDetails { CachedTokens = 60 },
            PromptCacheHitTokens = 70,
            PromptCacheMissTokens = 30
        };

        var result = _protocol.MapUsage(usage);

        result.CacheReadInputTokens.Should().Be(70,
            "DeepSeek format (prompt_cache_hit/miss) should take precedence over OpenAI format (PromptTokensDetails.CachedTokens)");
        result.CacheCreationInputTokens.Should().Be(30);
    }
}
