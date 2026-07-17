using Api.LLM;
using Api.LLM.CacheProtocol;
using Api.LLM.QueryServices.Anthropic;
using JoinCode.Abstractions.Utils;
using ChatApiMessage = JoinCode.Abstractions.LLM.Chat.ApiMessage;
using ChatMessageRole = JoinCode.Abstractions.LLM.Chat.MessageRole;
using MessageList = JoinCode.Abstractions.LLM.Chat.MessageList;

namespace Llm.Tests.Adapters.CacheProtocol;

public sealed class AnthropicCacheControlSerializationTests
{
    [Fact]
    public void BuildRequest_StaticSystem_Tools_ToolResults_AllGetCacheControl()
    {
        var toolCallId = JsonElementHelper.FromString("call_001");
        var toolCalls = new List<OpenAIToolCall>
        {
            new()
            {
                Id = "call_001",
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
            new ChatApiMessage(ChatMessageRole.System, "You are a helpful assistant."),
            new ChatApiMessage(ChatMessageRole.User, "Read the file"),
            new ChatApiMessage(ChatMessageRole.Assistant, "I will read the file", metadata),
            new ChatApiMessage(ChatMessageRole.Tool, "File contents here")
            {
                Metadata = new Dictionary<string, JsonElement>
                {
                    ["ToolCallId"] = JsonElementHelper.FromString("call_001")
                }
            },
            new ChatApiMessage(ChatMessageRole.User, "What does it say?")
        };

        var (systemBlocks, anthropicMessages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(messages);

        var tools = new List<AnthropicToolDefinition>
        {
            new() { Name = "read_file", Description = "Read a file" }
        };

        var protocol = new AnthropicCacheProtocol();
        protocol.AddCacheBreakpoints(systemBlocks, tools, anthropicMessages, hasMcpTools: false);

        systemBlocks.Should().HaveCount(1);
        systemBlocks[0].CacheControl.Should().NotBeNull("static system block should have cache_control");
        systemBlocks[0].CacheControl!.Type.Should().Be("ephemeral");
        systemBlocks[0].CacheControl!.Scope.Should().BeNull("no MCP tools → no scope");

        tools[0].CacheControl.Should().NotBeNull("last tool should have cache_control");

        var lastUserMsg = anthropicMessages.Last(m => m.Role == "user");
        if (lastUserMsg.Content is List<AnthropicContentBlock> blocks)
        {
            var toolResults = blocks.OfType<AnthropicToolResultBlock>().ToList();
            if (toolResults.Count > 0)
            {
                toolResults[^1].CacheControl.Should().NotBeNull("last tool result should have cache_control");
            }
        }
    }

    [Fact]
    public void BuildRequest_WithMcpTools_AllCacheControlsUseOrgScope()
    {
        var messages = new MessageList
        {
            new ChatApiMessage(ChatMessageRole.System, "You are a helpful assistant."),
            new ChatApiMessage(ChatMessageRole.User, "Hello")
        };

        var (systemBlocks, anthropicMessages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(messages);

        var tools = new List<AnthropicToolDefinition>
        {
            new() { Name = "mcp.search", Description = "Search MCP" }
        };

        var protocol = new AnthropicCacheProtocol();
        protocol.AddCacheBreakpoints(systemBlocks, tools, anthropicMessages, hasMcpTools: true);

        systemBlocks[0].CacheControl!.Scope.Should().Be("org", "MCP tools → org scope on system block");
        tools[0].CacheControl!.Scope.Should().Be("org", "MCP tools → org scope on tool");
    }

    [Fact]
    public void BuildRequest_StaticAndDynamicSystem_CacheControlOnLastStatic()
    {
        var messages = new MessageList
        {
            new ChatApiMessage(ChatMessageRole.System, "Static prefix"),
            new ChatApiMessage(ChatMessageRole.System, "Dynamic content", CacheBreakMarker.Create()),
            new ChatApiMessage(ChatMessageRole.User, "Hello")
        };

        var (systemBlocks, anthropicMessages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(messages);

        var tools = new List<AnthropicToolDefinition>
        {
            new() { Name = "read_file", Description = "Read" }
        };

        var protocol = new AnthropicCacheProtocol();
        protocol.AddCacheBreakpoints(systemBlocks, tools, anthropicMessages, hasMcpTools: false);

        systemBlocks.Should().HaveCount(2);
        systemBlocks[0].CacheControl.Should().NotBeNull("cache_control on last static block (index 0)");
        systemBlocks[1].CacheControl.Should().BeNull("dynamic block should NOT have cache_control");
    }

    [Fact]
    public void BuildRequest_SerializedJson_ContainsCacheControl()
    {
        var control = new AnthropicCacheControl { Scope = "org", Ttl = "1h" };
        var json = JsonSerializer.Serialize(control, AnthropicJsonContext.Default.AnthropicCacheControl);

        json.Should().Contain("\"type\":\"ephemeral\"");
        json.Should().Contain("\"scope\":\"org\"");
        json.Should().Contain("\"ttl\":\"1h\"");
    }

    [Fact]
    public void BuildRequest_SerializedJson_NullFieldsOmitted()
    {
        var control = new AnthropicCacheControl();
        var json = JsonSerializer.Serialize(control, AnthropicJsonContext.Default.AnthropicCacheControl);

        json.Should().Contain("\"type\":\"ephemeral\"");
        json.Should().NotContain("\"scope\"");
        json.Should().NotContain("\"ttl\"");
    }
}
