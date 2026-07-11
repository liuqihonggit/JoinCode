namespace Infra.Tests.LLM;

using Api.LLM;
using Api.LLM.QueryServices.Anthropic;
using JoinCode.Abstractions.Utils;
using ChatApiMessage = JoinCode.Abstractions.LLM.Chat.ApiMessage;
using ChatMessageRole = JoinCode.Abstractions.LLM.Chat.MessageRole;
using MessageList = JoinCode.Abstractions.LLM.Chat.MessageList;

/// <summary>
/// AnthropicQueryService.ConvertToAnthropicMessages Anthropic 角色转换测试
/// 验证 System CacheBreak 正确映射为 IsStatic，消息角色保持不变
/// </summary>
public sealed class QueryServiceAnthropicRoleConversionTests
{
    [Fact]
    public void SystemMessage_WithCacheBreak_SetsIsStaticFalse()
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["CacheBreak"] = JsonElementHelper.FromBoolean(true)
        };
        var messages = new MessageList
        {
            new ChatApiMessage(ChatMessageRole.System, "# 动态系统提示", metadata)
        };

        var (systemBlocks, anthropicMessages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(messages);

        systemBlocks.Should().HaveCount(1);
        systemBlocks[0].Text.Should().Be("# 动态系统提示");
        systemBlocks[0].IsStatic.Should().BeFalse("CacheBreak=true 应标记为非静态");
        anthropicMessages.Should().BeEmpty();
    }

    [Fact]
    public void SystemMessage_WithoutCacheBreak_SetsIsStaticTrue()
    {
        var messages = new MessageList
        {
            new ChatApiMessage(ChatMessageRole.System, "静态系统提示词")
        };

        var (systemBlocks, anthropicMessages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(messages);

        systemBlocks.Should().HaveCount(1);
        systemBlocks[0].IsStatic.Should().BeTrue("无 CacheBreak 应为静态");
    }

    [Fact]
    public void SystemMessage_WithCacheBreakFalse_SetsIsStaticTrue()
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["CacheBreak"] = JsonElementHelper.FromBoolean(false)
        };
        var messages = new MessageList
        {
            new ChatApiMessage(ChatMessageRole.System, "动态但未变化", metadata)
        };

        var (systemBlocks, anthropicMessages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(messages);

        systemBlocks.Should().HaveCount(1);
        systemBlocks[0].IsStatic.Should().BeTrue("CacheBreak=false 应为静态");
    }

    [Fact]
    public void UserMessage_ConvertedToAnthropicUserMessage()
    {
        var messages = new MessageList
        {
            new ChatApiMessage(ChatMessageRole.User, "用户输入")
        };

        var (systemBlocks, anthropicMessages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(messages);

        systemBlocks.Should().BeEmpty();
        anthropicMessages.Should().HaveCount(1);
        anthropicMessages[0].Role.Should().Be("user");
    }

    [Fact]
    public void AssistantMessage_WithToolCalls_ConvertedToToolUseBlocks()
    {
        var toolCalls = new List<OpenAIToolCall>
        {
            new OpenAIToolCall
            {
                Id = "call_123",
                Type = "function",
                Function = new OpenAIToolCallFunction
                {
                    Name = "read_file",
                    Arguments = "{\"path\":\"README.md\"}"
                }
            }
        };
        var metadata = new Dictionary<string, JsonElement>
        {
            ["ToolCalls"] = JsonElementHelper.FromObject(toolCalls, NativeJsonContext.Default.ListOpenAIToolCall)
        };
        var messages = new MessageList
        {
            new ChatApiMessage(ChatMessageRole.Assistant, "我来读取文件", metadata)
        };

        var (systemBlocks, anthropicMessages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(messages);

        systemBlocks.Should().BeEmpty();
        anthropicMessages.Should().HaveCount(1);
        anthropicMessages[0].Role.Should().Be("assistant");
    }

    [Fact]
    public void ThreePartMessages_StaticDynamicUser_CorrectStructure()
    {
        var staticMsg = new ChatApiMessage(ChatMessageRole.System, "静态前缀");
        var dynamicMsg = new ChatApiMessage(
            ChatMessageRole.System,
            "# 动态工具提示",
            new Dictionary<string, JsonElement> { ["CacheBreak"] = JsonElementHelper.FromBoolean(true) });
        var userMsg = new ChatApiMessage(ChatMessageRole.User, "读取文件");

        var messages = new MessageList { staticMsg, dynamicMsg, userMsg };
        var (systemBlocks, anthropicMessages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(messages);

        systemBlocks.Should().HaveCount(2);
        systemBlocks[0].IsStatic.Should().BeTrue("第一个 system 应为静态");
        systemBlocks[1].IsStatic.Should().BeFalse("第二个 system 带 CacheBreak 应为动态");
        anthropicMessages.Should().HaveCount(1);
        anthropicMessages[0].Role.Should().Be("user");
    }
}
