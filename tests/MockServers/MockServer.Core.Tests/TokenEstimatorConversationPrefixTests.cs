namespace MockServer.Core.Tests;

/// <summary>
/// TokenEstimator.ExtractConversationPrefix 测试
///
/// 背景: 真实多轮对话中, system prompt 保持不变, messages 数组逐步增长:
/// - Turn 1: [system, user1]
/// - Turn 2: [system, user1, assistant1, user2]
/// - Turn 3: [system, user1, assistant1, user2, assistant2, user3]
///
/// 真实 LLM Prefix Cache 基于完整对话前缀匹配:
/// - Turn 1: 完整 miss, 缓存 [system, user1]
/// - Turn 2: 部分命中 ([system, user1] 已缓存, [assistant1, user2] 是新增)
/// - Turn 3: 部分命中 ([system, user1, assistant1, user2] 已缓存, [assistant2, user3] 新增)
///
/// 旧实现 ExtractSystemPrefix 只提取 system prompt, 无法模拟此场景:
/// - Turn 1/2/3 的 system 相同 → 全部判定为"完全命中" (错误!)
///
/// 修复目标: ExtractConversationPrefix 提取 system + 所有消息内容, 作为完整前缀
/// </summary>
public sealed class TokenEstimatorConversationPrefixTests
{
    [Fact]
    public void OpenAIFormat_SingleSystemMessage_PrefixIsSystemContent()
    {
        var json = """{"messages":[{"role":"system","content":"You are helpful."}]}""";
        var req = JsonDocument.Parse(json).RootElement.Clone();

        var prefix = TokenEstimator.ExtractConversationPrefix(req);

        prefix.Should().Contain("You are helpful.");
    }

    [Fact]
    public void OpenAIFormat_SystemPlusUser_PrefixIncludesBoth()
    {
        var json = """{"messages":[{"role":"system","content":"SYS"},{"role":"user","content":"Hello"}]}""";
        var req = JsonDocument.Parse(json).RootElement.Clone();

        var prefix = TokenEstimator.ExtractConversationPrefix(req);

        prefix.Should().Contain("SYS");
        prefix.Should().Contain("Hello");
        prefix.IndexOf("SYS", StringComparison.Ordinal).Should().BeLessThan(prefix.IndexOf("Hello", StringComparison.Ordinal));
    }

    [Fact]
    public void AnthropicFormat_SystemFieldPlusMessages_PrefixIncludesBoth()
    {
        var json = """{"system":"SYS","messages":[{"role":"user","content":"Hello"}]}""";
        var req = JsonDocument.Parse(json).RootElement.Clone();

        var prefix = TokenEstimator.ExtractConversationPrefix(req);

        prefix.Should().Contain("SYS");
        prefix.Should().Contain("Hello");
        prefix.IndexOf("SYS", StringComparison.Ordinal).Should().BeLessThan(prefix.IndexOf("Hello", StringComparison.Ordinal));
    }

    [Fact]
    public void MultiTurn_SameSystemGrowingMessages_PrefixGrows()
    {
        // Turn 1: system + user1
        var turn1Json = """{"system":"SYS","messages":[{"role":"user","content":"question1"}]}""";
        // Turn 2: system + user1 + assistant1 + user2
        var turn2Json = """{"system":"SYS","messages":[{"role":"user","content":"question1"},{"role":"assistant","content":"answer1"},{"role":"user","content":"question2"}]}""";

        var prefix1 = TokenEstimator.ExtractConversationPrefix(JsonDocument.Parse(turn1Json).RootElement.Clone());
        var prefix2 = TokenEstimator.ExtractConversationPrefix(JsonDocument.Parse(turn2Json).RootElement.Clone());

        // Turn 2 prefix 应以 Turn 1 prefix 为前缀 (前缀增长)
        prefix2.StartsWith(prefix1, StringComparison.Ordinal).Should().BeTrue(
            "多轮对话中 Turn 2 的前缀应包含 Turn 1 的全部内容, turn2 应以 turn1 为前缀");
        prefix2.Length.Should().BeGreaterThan(prefix1.Length);
    }

    [Fact]
    public void MultiTurn_SameSystemSameMessages_PrefixEqual()
    {
        var json = """{"system":"SYS","messages":[{"role":"user","content":"question1"}]}""";

        var prefix1 = TokenEstimator.ExtractConversationPrefix(JsonDocument.Parse(json).RootElement.Clone());
        var prefix2 = TokenEstimator.ExtractConversationPrefix(JsonDocument.Parse(json).RootElement.Clone());

        prefix1.Should().Be(prefix2);
    }

    [Fact]
    public void DifferentUserContent_PrefixDifferent()
    {
        var json1 = """{"system":"SYS","messages":[{"role":"user","content":"question1"}]}""";
        var json2 = """{"system":"SYS","messages":[{"role":"user","content":"question2"}]}""";

        var prefix1 = TokenEstimator.ExtractConversationPrefix(JsonDocument.Parse(json1).RootElement.Clone());
        var prefix2 = TokenEstimator.ExtractConversationPrefix(JsonDocument.Parse(json2).RootElement.Clone());

        prefix1.Should().NotBe(prefix2);
    }

    [Fact]
    public void AnthropicContentBlocks_ExtractsTextFromBlocks()
    {
        // Anthropic content 可以是数组 (content blocks)
        var json = """{"system":"SYS","messages":[{"role":"user","content":[{"type":"text","text":"block content"}]}]}""";
        var req = JsonDocument.Parse(json).RootElement.Clone();

        var prefix = TokenEstimator.ExtractConversationPrefix(req);

        prefix.Should().Contain("block content");
    }

    [Fact]
    public void EmptyRequest_ReturnsEmptyString()
    {
        var json = """{}""";
        var req = JsonDocument.Parse(json).RootElement.Clone();

        var prefix = TokenEstimator.ExtractConversationPrefix(req);

        prefix.Should().BeEmpty();
    }

    // === Responses API 格式 (input 数组 + instructions 字段) ===

    [Fact]
    public void ResponsesFormat_InstructionsPlusInput_PrefixIncludesBoth()
    {
        var json = """{"instructions":"SYS","input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"Hello"}]}]}""";
        var req = JsonDocument.Parse(json).RootElement.Clone();

        var prefix = TokenEstimator.ExtractConversationPrefix(req);

        prefix.Should().Contain("SYS");
        prefix.Should().Contain("Hello");
        prefix.IndexOf("SYS", StringComparison.Ordinal).Should().BeLessThan(prefix.IndexOf("Hello", StringComparison.Ordinal));
    }

    [Fact]
    public void ResponsesFormat_InputOnly_PrefixIncludesInputContent()
    {
        var json = """{"input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"echo hello"}]}]}""";
        var req = JsonDocument.Parse(json).RootElement.Clone();

        var prefix = TokenEstimator.ExtractConversationPrefix(req);

        prefix.Should().Contain("echo hello");
    }

    [Fact]
    public void ResponsesFormat_MultiTurn_PrefixGrows()
    {
        var turn1Json = """{"instructions":"SYS","input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"q1"}]}]}""";
        var turn2Json = """{"instructions":"SYS","input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"q1"}]},{"type":"message","role":"assistant","content":[{"type":"output_text","text":"a1"}]},{"type":"message","role":"user","content":[{"type":"input_text","text":"q2"}]}]}""";

        var prefix1 = TokenEstimator.ExtractConversationPrefix(JsonDocument.Parse(turn1Json).RootElement.Clone());
        var prefix2 = TokenEstimator.ExtractConversationPrefix(JsonDocument.Parse(turn2Json).RootElement.Clone());

        prefix2.StartsWith(prefix1, StringComparison.Ordinal).Should().BeTrue(
            "Responses API 多轮对话 Turn 2 前缀应以 Turn 1 为前缀");
        prefix2.Length.Should().BeGreaterThan(prefix1.Length);
    }

    [Fact]
    public void ResponsesFormat_EstimateFromMessages_ReturnsNonZero()
    {
        var json = """{"instructions":"You are helpful.","input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"echo hello"}]}]}""";
        var req = JsonDocument.Parse(json).RootElement.Clone();

        var tokens = TokenEstimator.EstimateFromMessages(req);

        tokens.Should().BeGreaterThan(0, "Responses API 请求应估算出非零 token 数");
    }

    [Fact]
    public void ResponsesFormat_AssistantOutputText_ExtractedAsPrefix()
    {
        var json = """{"input":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"response text"}]}]}""";
        var req = JsonDocument.Parse(json).RootElement.Clone();

        var prefix = TokenEstimator.ExtractConversationPrefix(req);

        prefix.Should().Contain("response text");
    }
}
