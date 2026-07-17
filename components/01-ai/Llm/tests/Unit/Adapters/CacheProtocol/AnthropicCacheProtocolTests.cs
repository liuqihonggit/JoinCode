using Api.LLM.CacheProtocol;

namespace Llm.Tests.Adapters.CacheProtocol;

public sealed class AnthropicCacheProtocolTests
{
    private readonly AnthropicCacheProtocol _protocol = new();

    [Fact]
    public void RequiresExplicitCacheMarkers_ShouldBeTrue()
    {
        _protocol.RequiresExplicitCacheMarkers.Should().BeTrue(
            "Anthropic requires explicit cache_control markers on messages");
    }

    [Fact]
    public void DefaultCacheScope_ShouldBeEphemeral()
    {
        _protocol.DefaultCacheScope.Should().Be("ephemeral");
    }

    [Fact]
    public void CreateCacheControl_WithoutMcpTools_ScopeShouldBeNull()
    {
        var control = _protocol.CreateCacheControl(hasMcpTools: false);

        control.Type.Should().Be("ephemeral");
        control.Scope.Should().BeNull(
            "without MCP tools, Anthropic cache_control should not specify scope");
    }

    [Fact]
    public void CreateCacheControl_WithMcpTools_ScopeShouldBeOrg()
    {
        var control = _protocol.CreateCacheControl(hasMcpTools: true);

        control.Type.Should().Be("ephemeral");
        control.Scope.Should().Be("org",
            "with MCP tools, Anthropic cache_control should use org scope for cross-session caching");
    }

    [Fact]
    public void ResolveScope_WithoutMcpTools_ShouldBeNull()
    {
        _protocol.ResolveScope(hasMcpTools: false).Should().BeNull();
    }

    [Fact]
    public void ResolveScope_WithMcpTools_ShouldBeOrg()
    {
        _protocol.ResolveScope(hasMcpTools: true).Should().Be("org");
    }

    [Fact]
    public void IsStaticSystemBlock_NoMetadata_ShouldBeTrue()
    {
        var msg = new ApiMessage(MessageRole.System, "static content");

        _protocol.IsStaticSystemBlock(msg).Should().BeTrue(
            "system block without metadata is static and eligible for cache_control");
    }

    [Fact]
    public void IsStaticSystemBlock_WithCacheBreakMarker_ShouldBeFalse()
    {
        var msg = new ApiMessage(MessageRole.System, "dynamic content")
        {
            Metadata = CacheBreakMarker.Create()
        };

        _protocol.IsStaticSystemBlock(msg).Should().BeFalse(
            "system block with CacheBreak marker is dynamic and should NOT receive cache_control");
    }

    [Fact]
    public void IsStaticSystemBlock_WithOtherMetadata_ShouldBeTrue()
    {
        var msg = new ApiMessage(MessageRole.System, "content")
        {
            Metadata = new Dictionary<string, JsonElement>
            {
                ["OtherKey"] = JsonElementHelper.FromString("value")
            }
        };

        _protocol.IsStaticSystemBlock(msg).Should().BeTrue(
            "system block with non-CacheBreak metadata is still static");
    }

    [Fact]
    public void MapUsage_WithCacheStats_ShouldMapCorrectly()
    {
        var usage = new AnthropicUsage
        {
            InputTokens = 100,
            OutputTokens = 50,
            CacheCreationInputTokens = 30,
            CacheReadInputTokens = 70
        };

        var result = _protocol.MapUsage(usage);

        result.PromptTokens.Should().Be(100);
        result.CompletionTokens.Should().Be(50);
        result.CacheCreationInputTokens.Should().Be(30);
        result.CacheReadInputTokens.Should().Be(70);
    }

    [Fact]
    public void MapUsage_WithoutCacheStats_ShouldDefaultToZero()
    {
        var usage = new AnthropicUsage
        {
            InputTokens = 100,
            OutputTokens = 50
        };

        var result = _protocol.MapUsage(usage);

        result.CacheCreationInputTokens.Should().Be(0);
        result.CacheReadInputTokens.Should().Be(0);
    }

    [Fact]
    public void MapUsage_WithReasoningTokens_ShouldMapCorrectly()
    {
        var usage = new AnthropicUsage
        {
            InputTokens = 100,
            OutputTokens = 50,
            OutputTokensDetails = new AnthropicOutputTokensDetails { ReasoningTokens = 20 }
        };

        var result = _protocol.MapUsage(usage);

        result.ReasoningTokens.Should().Be(20);
    }
}
