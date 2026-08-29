namespace Llm.Tests.Adapters.LLM.QueryServices.Anthropic;

public sealed class AnthropicQueryServiceTests
{
    #region ConvertToAnthropicMessages

    [Fact]
    public void ConvertToAnthropicMessages_SystemMessage_AddsSystemBlock()
    {
        var history = new MessageList { new(MessageRole.System, "sys") };

        var (system, messages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(history);

        system.Should().ContainSingle();
        system[0].Text.Should().Be("sys");
        messages.Should().BeEmpty();
    }

    [Fact]
    public void ConvertToAnthropicMessages_UserMessage_AddsUserMessage()
    {
        var history = new MessageList { new(MessageRole.User, "hi") };

        var (system, messages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(history);

        system.Should().BeEmpty();
        messages.Should().ContainSingle();
        messages[0].Role.Should().Be("user");
    }

    [Fact]
    public void ConvertToAnthropicMessages_AssistantMessage_AddsAssistantMessage()
    {
        var history = new MessageList { new(MessageRole.Assistant, "hello") };

        var (_, messages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(history);

        messages.Should().ContainSingle();
        messages[0].Role.Should().Be("assistant");
        messages[0].Content?.Text.Should().Be("hello");
    }

    [Fact]
    public void ConvertToAnthropicMessages_AssistantWithToolCalls_AddsContentBlocks()
    {
        var entries = new[]
        {
            new ToolCallEntry { Id = "call-1", Name = "ToolA", Arguments = "{}" }
        };
        var metadata = ToolCallEntry.BuildAssistantMetadata(entries);
        var history = new MessageList { new(MessageRole.Assistant, "using tool", metadata) };

        var (_, messages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(history);

        messages.Should().ContainSingle();
        var blocks = messages[0].Content?.Blocks;
        blocks.Should().NotBeNull();
        blocks!.Should().HaveCount(2);
        blocks[0].Should().BeOfType<AnthropicTextBlock>();
        blocks[1].Should().BeOfType<AnthropicToolUseBlock>();
        ((AnthropicToolUseBlock)blocks[1]).Name.Should().Be("ToolA");
    }

    [Fact]
    public void ConvertToAnthropicMessages_ToolResult_FlushesAsUserMessage()
    {
        var metadata = ToolCallEntry.BuildToolResultMetadata("call-1", "ToolA");
        var history = new MessageList
        {
            new(MessageRole.Tool, "result", metadata)
        };

        var (_, messages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(history);

        messages.Should().ContainSingle();
        messages[0].Role.Should().Be("user");
        var blocks = messages[0].Content?.Blocks;
        blocks.Should().NotBeNull();
        blocks![0].Should().BeOfType<AnthropicToolResultBlock>();
    }

    [Fact]
    public void ConvertToAnthropicMessages_UserAfterToolResult_CombinesIntoSingleUserMessage()
    {
        var toolMetadata = ToolCallEntry.BuildToolResultMetadata("call-1", "ToolA");
        var history = new MessageList
        {
            new(MessageRole.Tool, "result", toolMetadata),
            new(MessageRole.User, "follow up")
        };

        var (_, messages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(history);

        messages.Should().ContainSingle();
        var blocks = messages[0].Content?.Blocks;
        blocks.Should().NotBeNull();
        blocks!.Should().HaveCount(2);
        blocks[0].Should().BeOfType<AnthropicToolResultBlock>();
        blocks[1].Should().BeOfType<AnthropicToolResultBlock>();
    }

    #endregion

    #region ConvertAnthropicResponseToApiMessages

    [Fact]
    public void ConvertAnthropicResponseToApiMessages_TextBlock_ReturnsContent()
    {
        var response = new AnthropicMessagesResponse
        {
            Id = "msg-1",
            Model = "claude",
            Content =
            [
                new AnthropicResponseContentBlock { Type = AnthropicContentBlockType.Text, Text = "Hello" }
            ]
        };

        var result = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        result.Should().ContainSingle();
        result[0].Content.Should().Be("Hello");
        result[0].Role.Should().Be(MessageRole.Assistant);
    }

    [Fact]
    public void ConvertAnthropicResponseToApiMessages_ThinkingBlock_AddsThinkingMetadata()
    {
        var response = new AnthropicMessagesResponse
        {
            Id = "msg-1",
            Model = "claude",
            Content =
            [
                new AnthropicResponseContentBlock { Type = AnthropicContentBlockType.Thinking, Thinking = "think" }
            ]
        };

        var result = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        result[0].Metadata.Should().ContainKey("thinking_content");
        result[0].Metadata!["thinking_content"].GetString().Should().Be("think");
    }

    [Fact]
    public void ConvertAnthropicResponseToApiMessages_ToolUseBlock_AddsToolCallMetadata()
    {
        var response = new AnthropicMessagesResponse
        {
            Id = "msg-1",
            Model = "claude",
            Content =
            [
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.ToolUse,
                    Id = "call-1",
                    Name = "ToolA",
                    Input = JsonElementHelper.FromJson("{\"x\":1}")
                }
            ]
        };

        var result = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        var metadata = result[0].Metadata;
        metadata.Should().ContainKeys("AllToolCalls", "ToolCalls");
        metadata!["AllToolCalls"].ValueKind.Should().Be(JsonValueKind.Array);
        metadata!["AllToolCalls"].GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void ConvertAnthropicResponseToApiMessages_WebSearchResultArray_AppendsLinks()
    {
        var json = "[{\"title\":\"T\",\"url\":\"https://example.com\"}]";
        var response = new AnthropicMessagesResponse
        {
            Id = "msg-1",
            Model = "claude",
            Content =
            [
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.WebSearchToolResult,
                    Content = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(json))
                }
            ]
        };

        var result = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        result[0].Content.Should().Contain("[T](https://example.com)");
        result[0].Metadata.Should().ContainKey("web_search_results");
    }

    [Fact]
    public void ConvertAnthropicResponseToApiMessages_WebSearchError_AddsErrorText()
    {
        var element = JsonSerializer.SerializeToElement(new { error_code = "rate_limited" });
        var response = new AnthropicMessagesResponse
        {
            Id = "msg-1",
            Model = "claude",
            Content =
            [
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.WebSearchToolResult,
                    Content = element
                }
            ]
        };

        var result = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        result[0].Content.Should().Contain("rate_limited");
    }

    [Fact]
    public void ConvertAnthropicResponseToApiMessages_WithUsage_AddsUsageMetadata()
    {
        var response = new AnthropicMessagesResponse
        {
            Id = "msg-1",
            Model = "claude",
            Content = [],
            Usage = new AnthropicUsage
            {
                InputTokens = 10,
                OutputTokens = 5
            }
        };

        var result = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        result[0].Metadata.Should().ContainKey("Usage");
    }

    #endregion

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage());
    }

    #region Two-Phase Tool Loading — BuildAnthropicToolsFromKernel

    [Fact]
    public void BuildAnthropicToolsFromKernel_OnlyCoreTools_ToolsPopulated_ToolGroupsEmpty()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.CoreTools, [
            new ToolDef("read", "Read a file"),
            new ToolDef("write", "Write a file")
        ]));

        var (tools, toolGroups) = AnthropicQueryService.BuildAnthropicToolsFromKernel(kernel);

        tools.Should().HaveCount(2);
        tools.Select(t => t.Name).Should().Contain(["read", "write"]);
        toolGroups.Should().BeEmpty();
    }

    [Fact]
    public void BuildAnthropicToolsFromKernel_OnlyMcpTools_ToolsEmpty_ToolGroupsPopulated()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.server1.tool1", "MCP tool 1"),
            new ToolDef("mcp.server2.tool2", "MCP tool 2")
        ]));

        var (tools, toolGroups) = AnthropicQueryService.BuildAnthropicToolsFromKernel(kernel);

        tools.Should().BeEmpty();
        toolGroups.Should().ContainSingle();
        toolGroups[0].Name.Should().Be(ToolGroupNameConstants.McpTools);
        toolGroups[0].Tools.Should().Contain(["mcp.server1.tool1", "mcp.server2.tool2"]);
    }

    [Fact]
    public void BuildAnthropicToolsFromKernel_MixedTools_CoreToolsInTools_McpToolsInGroups()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.CoreTools, [
            new ToolDef("read", "Read a file")
        ]));
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1")
        ]));

        var (tools, toolGroups) = AnthropicQueryService.BuildAnthropicToolsFromKernel(kernel);

        tools.Should().ContainSingle();
        tools[0].Name.Should().Be("read");
        toolGroups.Should().ContainSingle();
        toolGroups[0].Name.Should().Be(ToolGroupNameConstants.McpTools);
        toolGroups[0].Tools.Should().ContainSingle().Which.Should().Be("mcp.tool1");
    }

    #endregion

    #region Two-Phase Tool Loading — CreateSecondAnthropicRequestWithDescriptions

    [Fact]
    public void CreateSecondAnthropicRequestWithDescriptions_ValidToolNames_BuildsDescriptions()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1"),
            new ToolDef("mcp.tool2", "MCP tool 2")
        ]));

        var originalRequest = new AnthropicMessagesRequest
        {
            Model = "claude-3",
            Messages = [new AnthropicMessage { Role = "user", Content = "hi" }],
            Stream = true
        };
        var descRequestContent = """{"tools":["mcp.tool1","mcp.tool2"]}""";

        var secondRequest = AnthropicQueryService.CreateSecondAnthropicRequestWithDescriptions(originalRequest, descRequestContent, kernel);

        secondRequest.ToolDescriptions.Should().HaveCount(2);
        secondRequest.ToolDescriptions!.Select(t => t.Name).Should().Contain(["mcp.tool1", "mcp.tool2"]);
        secondRequest.Model.Should().Be("claude-3");
        secondRequest.Stream.Should().BeTrue();
    }

    [Fact]
    public void CreateSecondAnthropicRequestWithDescriptions_UnknownToolNames_DescriptionsEmpty()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1")
        ]));

        var originalRequest = new AnthropicMessagesRequest { Model = "claude-3" };
        var descRequestContent = """{"tools":["nonexistent.tool"]}""";

        var secondRequest = AnthropicQueryService.CreateSecondAnthropicRequestWithDescriptions(originalRequest, descRequestContent, kernel);

        secondRequest.ToolDescriptions.Should().BeEmpty();
    }

    [Fact]
    public void CreateSecondAnthropicRequestWithDescriptions_PreservesOriginalFields()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1")
        ]));

        var originalRequest = new AnthropicMessagesRequest
        {
            Model = "claude-3",
            Messages = [new AnthropicMessage { Role = "user", Content = "test" }],
            Stream = true,
            Temperature = 0.7f,
            MaxTokens = 1000,
            Tools = [new AnthropicToolDefinition { Name = "read" }],
            ToolGroups = [new OpenAIToolGroup { Name = "mcp_tools", Tools = ["mcp.tool1"] }]
        };
        var descRequestContent = """{"tools":["mcp.tool1"]}""";

        var secondRequest = AnthropicQueryService.CreateSecondAnthropicRequestWithDescriptions(originalRequest, descRequestContent, kernel);

        secondRequest.Model.Should().Be("claude-3");
        secondRequest.Temperature.Should().Be(0.7f);
        secondRequest.MaxTokens.Should().Be(1000);
        secondRequest.Tools.Should().BeSameAs(originalRequest.Tools);
        secondRequest.ToolGroups.Should().BeSameAs(originalRequest.ToolGroups);
        secondRequest.ToolDescriptions.Should().ContainSingle();
    }

    #endregion
}
