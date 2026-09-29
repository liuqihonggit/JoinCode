namespace Llm.Tests.Adapters.LLM.QueryServices.Anthropic;

public sealed class AnthropicQueryServiceTests {
    #region ConvertToAnthropicMessages

    [Fact]
    public void ConvertToAnthropicMessages_SystemMessage_AddsSystemBlock() {
        var history = new MessageList { new(MessageRole.System, "sys") };

        var (system, messages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(history);

        system.Should().ContainSingle();
        system[0].Text.Should().Be("sys");
        messages.Should().BeEmpty();
    }

    [Fact]
    public void ConvertToAnthropicMessages_UserMessage_AddsUserMessage() {
        var history = new MessageList { new(MessageRole.User, "hi") };

        var (system, messages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(history);

        system.Should().BeEmpty();
        messages.Should().ContainSingle();
        messages[0].Role.Should().Be("user");
    }

    [Fact]
    public void ConvertToAnthropicMessages_AssistantMessage_AddsAssistantMessage() {
        var history = new MessageList { new(MessageRole.Assistant, "hello") };

        var (_, messages) = AnthropicQueryService.ConvertToAnthropicMessagesPublic(history);

        messages.Should().ContainSingle();
        messages[0].Role.Should().Be("assistant");
        messages[0].Content?.Text.Should().Be("hello");
    }

    [Fact]
    public void ConvertToAnthropicMessages_AssistantWithToolCalls_AddsContentBlocks() {
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
    public void ConvertToAnthropicMessages_ToolResult_FlushesAsUserMessage() {
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
    public void ConvertToAnthropicMessages_UserAfterToolResult_CombinesIntoSingleUserMessage() {
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
    public void ConvertAnthropicResponseToApiMessages_TextBlock_ReturnsContent() {
        var response = new AnthropicMessagesResponse {
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
    public void ConvertAnthropicResponseToApiMessages_ThinkingBlock_AddsThinkingMetadata() {
        var response = new AnthropicMessagesResponse {
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
    public void ConvertAnthropicResponseToApiMessages_ToolUseBlock_AddsToolCallMetadata() {
        var response = new AnthropicMessagesResponse {
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
    public void ConvertAnthropicResponseToApiMessages_WebSearchResultArray_AppendsLinks() {
        var json = "[{\"title\":\"T\",\"url\":\"https://example.com\"}]";
        var response = new AnthropicMessagesResponse {
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
    public void ConvertAnthropicResponseToApiMessages_WebSearchError_AddsErrorText() {
        var element = JsonSerializer.SerializeToElement(new { error_code = "rate_limited" });
        var response = new AnthropicMessagesResponse {
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
    public void ConvertAnthropicResponseToApiMessages_WithUsage_AddsUsageMetadata() {
        var response = new AnthropicMessagesResponse {
            Id = "msg-1",
            Model = "claude",
            Content = [],
            Usage = new AnthropicUsage {
                InputTokens = 10,
                OutputTokens = 5
            }
        };

        var result = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        result[0].Metadata.Should().ContainKey("Usage");
    }

    #endregion

    private sealed class FakeHttpMessageHandler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage());
    }

    #region Two-Phase Tool Loading — BuildAnthropicToolsFromKernel

    [Fact]
    public async Task BuildAnthropicToolsFromKernel_OnlyCoreTools_ToolsPopulated_ToolGroupsEmpty() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
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
    public async Task BuildAnthropicToolsFromKernel_OnlyMcpTools_ToolsEmpty_ToolGroupsPopulated() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
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
    public async Task BuildAnthropicToolsFromKernel_MixedTools_CoreToolsInTools_McpToolsInGroups() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
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
    public async Task CreateSecondAnthropicRequestWithDescriptions_ValidToolNames_BuildsDescriptions() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1"),
            new ToolDef("mcp.tool2", "MCP tool 2")
        ]));

        var originalRequest = new AnthropicMessagesRequest {
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
    public async Task CreateSecondAnthropicRequestWithDescriptions_UnknownToolNames_DescriptionsEmpty() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1")
        ]));

        var originalRequest = new AnthropicMessagesRequest { Model = "claude-3" };
        var descRequestContent = """{"tools":["nonexistent.tool"]}""";

        var secondRequest = AnthropicQueryService.CreateSecondAnthropicRequestWithDescriptions(originalRequest, descRequestContent, kernel);

        secondRequest.ToolDescriptions.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateSecondAnthropicRequestWithDescriptions_PreservesOriginalFields() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1")
        ]));

        var originalRequest = new AnthropicMessagesRequest {
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

    #region FilterDeferredTools — 复杂双集合匹配(已发现 vs 未发现)

    private static AnthropicToolDefinition MakeTool(string name) =>
        new() { Name = name, InputSchema = new AnthropicInputSchema() };

    [Fact]
    public void FilterDeferredTools_NoDeferredTools_ReturnsAllToolsAsIs() {
        var allTools = new List<AnthropicToolDefinition> { MakeTool("a"), MakeTool("b") };

        var result = AnthropicQueryService.FilterDeferredTools(allTools, [], []);

        result.Should().HaveCount(2);
        result.Select(t => t.Name).Should().Contain(["a", "b"]);
    }

    [Fact]
    public void FilterDeferredTools_DeferredToolAlreadyDiscovered_KeepsOriginalSchema() {
        var allTools = new List<AnthropicToolDefinition> { MakeTool("deferred_tool") };
        var deferred = new List<DeferredToolInfo> { new("deferred_tool", "desc") };
        var discovered = new List<string> { "deferred_tool" };

        var result = AnthropicQueryService.FilterDeferredTools(allTools, deferred, discovered);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("deferred_tool");
        result[0].InputSchema.Should().NotBeNull("已发现工具保留原 schema");
        result[0].DeferLoading.Should().BeNull();
    }

    [Fact]
    public void FilterDeferredTools_DeferredToolNotDiscovered_ReplacesWithPlaceholderAndToolSearch() {
        var allTools = new List<AnthropicToolDefinition> { MakeTool("hidden_tool") };
        var deferred = new List<DeferredToolInfo> { new("hidden_tool", "hidden desc") };
        var discovered = new List<string>();

        var result = AnthropicQueryService.FilterDeferredTools(allTools, deferred, discovered);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("hidden_tool");
        result[0].DeferLoading.Should().BeTrue("未发现 deferred 工具替换为占位定义");
        result[0].InputSchema.Should().BeNull();
        result[1].Name.Should().Be("tool_search", "追加 tool_search 工具");
    }

    [Fact]
    public void FilterDeferredTools_MixedDiscoveredAndNotDiscovered_OnlyUndiscoveredReplaced() {
        var allTools = new List<AnthropicToolDefinition> {
            MakeTool("found_tool"),
            MakeTool("missing_tool")
        };
        var deferred = new List<DeferredToolInfo> {
            new("found_tool", "d1"),
            new("missing_tool", "d2")
        };
        var discovered = new List<string> { "found_tool" };

        var result = AnthropicQueryService.FilterDeferredTools(allTools, deferred, discovered);

        result.Should().HaveCount(3);
        result[0].Name.Should().Be("found_tool");
        result[0].DeferLoading.Should().BeNull();
        result[1].Name.Should().Be("missing_tool");
        result[1].DeferLoading.Should().BeTrue();
        result[2].Name.Should().Be("tool_search");
    }

    [Fact]
    public void FilterDeferredTools_NonDeferredToolAlwaysKept() {
        var allTools = new List<AnthropicToolDefinition> { MakeTool("regular_tool") };
        var deferred = new List<DeferredToolInfo> { new("other_tool", "d") };
        var discovered = new List<string>();

        var result = AnthropicQueryService.FilterDeferredTools(allTools, deferred, discovered);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("regular_tool");
        result[0].DeferLoading.Should().BeNull("非 deferred 工具原样保留");
    }

    [Fact]
    public void FilterDeferredTools_AllDeferredDiscovered_NoToolSearchAdded() {
        var allTools = new List<AnthropicToolDefinition> { MakeTool("d1"), MakeTool("d2") };
        var deferred = new List<DeferredToolInfo> { new("d1", "x"), new("d2", "y") };
        var discovered = new List<string> { "d1", "d2" };

        var result = AnthropicQueryService.FilterDeferredTools(allTools, deferred, discovered);

        result.Should().HaveCount(2);
        result.Select(t => t.Name).Should().NotContain("tool_search");
    }

    #endregion

    #region ConvertContextManagement — 2策略 switch 映射

    [Fact]
    public void ConvertContextManagement_ClearToolUsesStrategy_MapsToAnthropicClearToolUses() {
        var config = new ContextManagementConfig {
            Edits = [
                new ClearToolUsesStrategy {
                    Trigger = new ContextTrigger { Type = "token_threshold", Value = 10000 },
                    Keep = new ContextKeep { Type = "recent_tool_uses", Value = 3 },
                    ExcludeTools = ["grep"]
                }
            ]
        };

        var result = AnthropicQueryService.ConvertContextManagement(config);

        result.Edits.Should().ContainSingle();
        result.Edits[0].Should().BeOfType<AnthropicClearToolUsesStrategy>();
        var clear = (AnthropicClearToolUsesStrategy)result.Edits[0];
        clear.Trigger!.Type.Should().Be("token_threshold");
        clear.Trigger.Value.Should().Be(10000);
        clear.Keep!.Value.Should().Be(3);
        clear.ExcludeTools.Should().Contain("grep");
    }

    [Fact]
    public void ConvertContextManagement_ClearThinkingStrategy_MapsToAnthropicClearThinking() {
        var config = new ContextManagementConfig {
            Edits = [new ClearThinkingStrategy { Keep = "all" }]
        };

        var result = AnthropicQueryService.ConvertContextManagement(config);

        result.Edits.Should().ContainSingle();
        result.Edits[0].Should().BeOfType<AnthropicClearThinkingStrategy>();
    }

    [Fact]
    public void ConvertContextManagement_ClearToolUsesWithNullOptionalFields_MapsWithoutCrash() {
        var config = new ContextManagementConfig {
            Edits = [new ClearToolUsesStrategy()]
        };

        var result = AnthropicQueryService.ConvertContextManagement(config);

        var clear = (AnthropicClearToolUsesStrategy)result.Edits[0];
        clear.Trigger.Should().BeNull();
        clear.Keep.Should().BeNull();
        clear.ExcludeTools.Should().BeEmpty();
    }

    [Fact]
    public void ConvertContextManagement_UnknownStrategy_ThrowsInvalidOperationException() {
        var config = new ContextManagementConfig {
            Edits = [new UnknownStrategy()]
        };

        var act = () => AnthropicQueryService.ConvertContextManagement(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown ContextEditStrategy*");
    }

    [Fact]
    public void ConvertContextManagement_MultipleStrategies_AllMapped() {
        var config = new ContextManagementConfig {
            Edits = [
                new ClearToolUsesStrategy(),
                new ClearThinkingStrategy { Keep = "all" }
            ]
        };

        var result = AnthropicQueryService.ConvertContextManagement(config);

        result.Edits.Should().HaveCount(2);
        result.Edits[0].Should().BeOfType<AnthropicClearToolUsesStrategy>();
        result.Edits[1].Should().BeOfType<AnthropicClearThinkingStrategy>();
    }

    private sealed class UnknownStrategy : ContextEditStrategy {
        public override string Type => "unknown_strategy";
    }

    #endregion

    #region CreateToolResultBlock — image/text 多模态分支

    [Fact]
    public void CreateToolResultBlock_NoContentBlocks_ContentFromString() {
        var msg = new ApiMessage(MessageRole.Tool, "plain text",
            new Dictionary<string, JsonElement> {
                ["ToolCallId"] = JsonElementHelper.FromString("call-1")
            });

        var block = AnthropicQueryService.CreateToolResultBlock(msg);

        block.ToolUseId.Should().Be("call-1");
        block.Content!.Value.ValueKind.Should().Be(JsonValueKind.String);
        block.Content.Value.GetString().Should().Be("plain text");
    }

    [Fact]
    public void CreateToolResultBlock_TextContentBlock_EmitsTextBlock() {
        var msg = new ApiMessage {
            Role = MessageRole.Tool,
            Content = "fallback",
            ContentBlocks = [new ToolContent { Type = ToolContentType.Text, Text = "block text" }]
        };

        var block = AnthropicQueryService.CreateToolResultBlock(msg);

        block.Content!.Value.ValueKind.Should().Be(JsonValueKind.Array);
        var raw = block.Content.Value.GetRawText();
        raw.Should().Contain("\"type\":\"text\"");
        raw.Should().Contain("\"text\":\"block text\"");
    }

    [Fact]
    public void CreateToolResultBlock_ImageContentBlock_EmitsImageBlock() {
        var msg = new ApiMessage {
            Role = MessageRole.Tool,
            Content = "fallback",
            ContentBlocks = [new ToolContent {
                Type = ToolContentType.Image,
                Data = "base64data",
                MimeType = "image/png"
            }]
        };

        var block = AnthropicQueryService.CreateToolResultBlock(msg);

        var raw = block.Content!.Value.GetRawText();
        raw.Should().Contain("\"type\":\"image\"");
        raw.Should().Contain("\"media_type\":\"image/png\"");
        raw.Should().Contain("\"data\":\"base64data\"");
    }

    [Fact]
    public void CreateToolResultBlock_MixedImageAndText_BothBlocksEmitted() {
        var msg = new ApiMessage {
            Role = MessageRole.Tool,
            Content = "fallback",
            ContentBlocks = [
                new ToolContent { Type = ToolContentType.Text, Text = "desc" },
                new ToolContent { Type = ToolContentType.Image, Data = "d", MimeType = "image/jpeg" }
            ]
        };

        var block = AnthropicQueryService.CreateToolResultBlock(msg);

        block.Content!.Value.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void CreateToolResultBlock_EmptyContentBlocks_FallsBackToStringContent() {
        var msg = new ApiMessage {
            Role = MessageRole.Tool,
            Content = "only string",
            ContentBlocks = []
        };

        var block = AnthropicQueryService.CreateToolResultBlock(msg);

        block.Content!.Value.GetString().Should().Be("only string");
    }

    [Fact]
    public void CreateToolResultBlock_NoToolCallIdMetadata_ToolUseIdEmpty() {
        var msg = new ApiMessage(MessageRole.Tool, "x");

        var block = AnthropicQueryService.CreateToolResultBlock(msg);

        block.ToolUseId.Should().BeEmpty();
    }

    #endregion

    #region ExtractQueryFromPartialJson — regex 提取 + 3次 Replace 转义还原

    [Fact]
    public void ExtractQueryFromPartialJson_NoQueryField_ReturnsNull() {
        AnthropicQueryService.ExtractQueryFromPartialJson("""{"other":"value"}""").Should().BeNull();
    }

    [Fact]
    public void ExtractQueryFromPartialJson_SimpleQuery_ReturnsValue() {
        AnthropicQueryService.ExtractQueryFromPartialJson("""{"query":"hello"}""").Should().Be("hello");
    }

    [Fact]
    public void ExtractQueryFromPartialJson_EscapedQuote_RestoredToQuote() {
        var result = AnthropicQueryService.ExtractQueryFromPartialJson("""{"query":"a\"b"}""");
        result.Should().Be("a\"b");
    }

    [Fact]
    public void ExtractQueryFromPartialJson_EscapedBackslash_RestoredToBackslash() {
        var result = AnthropicQueryService.ExtractQueryFromPartialJson("""{"query":"a\\b"}""");
        result.Should().Be("a\\b");
    }

    [Fact]
    public void ExtractQueryFromPartialJson_EscapedNewline_RestoredToNewline() {
        var result = AnthropicQueryService.ExtractQueryFromPartialJson("""{"query":"a\nb"}""");
        result.Should().Be("a\nb");
    }

    [Fact]
    public void ExtractQueryFromPartialJson_AllEscapesCombined_AllRestored() {
        var result = AnthropicQueryService.ExtractQueryFromPartialJson("""{"query":"a\"b\\c\nd"}""");
        result.Should().Be("a\"b\\c\nd");
    }

    [Fact]
    public void ExtractQueryFromPartialJson_QueryWithSpacesInKey_Matched() {
        var result = AnthropicQueryService.ExtractQueryFromPartialJson("""{"query" : "spaced"}""");
        result.Should().Be("spaced");
    }

    [Fact]
    public void ExtractQueryFromPartialJson_EmptyQueryValue_ReturnsEmptyString() {
        AnthropicQueryService.ExtractQueryFromPartialJson("""{"query":""}""").Should().Be("");
    }

    #endregion

    #region ProcessWebSearchToolResult — array/error 分支

    [Fact]
    public void ProcessWebSearchToolResult_ArrayContent_ExtractsLinksAndRawJson() {
        var block = new AnthropicResponseContentBlock {
            Type = AnthropicContentBlockType.WebSearchToolResult,
            Content = JsonElementHelper.FromJson("""[{"title":"T","url":"https://x"}]""")
        };
        var textParts = new StringBuilder();
        var webSearchResults = new List<string>();

        AnthropicQueryService.ProcessWebSearchToolResult(block, textParts, webSearchResults);

        textParts.ToString().Should().Contain("[T](https://x)");
        webSearchResults.Should().ContainSingle();
    }

    [Fact]
    public void ProcessWebSearchToolResult_ErrorObjectContent_AppendsErrorText() {
        var block = new AnthropicResponseContentBlock {
            Type = AnthropicContentBlockType.WebSearchToolResult,
            Content = JsonElementHelper.FromJson("""{"error_code":"rate_limited"}""")
        };
        var textParts = new StringBuilder();
        var webSearchResults = new List<string>();

        AnthropicQueryService.ProcessWebSearchToolResult(block, textParts, webSearchResults);

        textParts.ToString().Should().Contain("Web search error: rate_limited");
        webSearchResults.Should().BeEmpty();
    }

    [Fact]
    public void ProcessWebSearchToolResult_NullContent_NoOp() {
        var block = new AnthropicResponseContentBlock {
            Type = AnthropicContentBlockType.WebSearchToolResult,
            Content = null
        };
        var textParts = new StringBuilder();
        var webSearchResults = new List<string>();

        AnthropicQueryService.ProcessWebSearchToolResult(block, textParts, webSearchResults);

        textParts.ToString().Should().BeEmpty();
        webSearchResults.Should().BeEmpty();
    }

    [Fact]
    public void ProcessWebSearchToolResult_ArrayWithMissingTitleUrl_SkipsIncompleteEntries() {
        var block = new AnthropicResponseContentBlock {
            Type = AnthropicContentBlockType.WebSearchToolResult,
            Content = JsonElementHelper.FromJson("""[{"title":"T"},{"url":"https://x"}]""")
        };
        var textParts = new StringBuilder();
        var webSearchResults = new List<string>();

        AnthropicQueryService.ProcessWebSearchToolResult(block, textParts, webSearchResults);

        textParts.ToString().Should().BeEmpty("title 和 url 都缺失的条目应跳过");
        webSearchResults.Should().ContainSingle();
    }

    [Fact]
    public void ProcessWebSearchToolResult_ErrorWithoutErrorCode_DefaultsToUnknown() {
        var block = new AnthropicResponseContentBlock {
            Type = AnthropicContentBlockType.WebSearchToolResult,
            Content = JsonElementHelper.FromJson("""{}""")
        };
        var textParts = new StringBuilder();

        AnthropicQueryService.ProcessWebSearchToolResult(block, textParts, new List<string>());

        textParts.ToString().Should().Contain("Web search error: unknown");
    }

    #endregion

    #region BuildWebSearchResultMetadata — array/object 分支 + links 拼接

    [Fact]
    public void BuildWebSearchResultMetadata_ArrayContent_AddsSearchLinks() {
        var block = new AnthropicResponseContentBlock {
            Type = AnthropicContentBlockType.WebSearchToolResult,
            Id = "wsu-1",
            Content = JsonElementHelper.FromJson("""[{"title":"A","url":"https://a"},{"title":"B","url":"https://b"}]""")
        };

        var metadata = AnthropicQueryService.BuildWebSearchResultMetadata(block, "msg-1", "claude");

        metadata["Id"].GetString().Should().Be("msg-1");
        metadata["Model"].GetString().Should().Be("claude");
        metadata["web_search_result"].GetBoolean().Should().BeTrue();
        metadata["tool_use_id"].GetString().Should().Be("wsu-1");
        metadata.Should().ContainKey("search_links");
        metadata["search_links"].GetString().Should().Contain("[A](https://a)");
        metadata["search_links"].GetString().Should().Contain("[B](https://b)");
    }

    [Fact]
    public void BuildWebSearchResultMetadata_ObjectErrorContent_AddsSearchError() {
        var block = new AnthropicResponseContentBlock {
            Type = AnthropicContentBlockType.WebSearchToolResult,
            Content = JsonElementHelper.FromJson("""{"error_code":"timeout"}""")
        };

        var metadata = AnthropicQueryService.BuildWebSearchResultMetadata(block, "m", "model");

        metadata.Should().ContainKey("search_error");
        metadata["search_error"].GetString().Should().Be("timeout");
        metadata.Should().NotContainKey("search_links");
    }

    [Fact]
    public void BuildWebSearchResultMetadata_NullContent_OnlyBaseMetadata() {
        var block = new AnthropicResponseContentBlock {
            Type = AnthropicContentBlockType.WebSearchToolResult,
            Content = null
        };

        var metadata = AnthropicQueryService.BuildWebSearchResultMetadata(block, "m", "model");

        metadata.Should().HaveCount(4);
        metadata.Should().NotContainKey("search_links");
        metadata.Should().NotContainKey("search_error");
    }

    [Fact]
    public void BuildWebSearchResultMetadata_EmptyArray_NoSearchLinksKey() {
        var block = new AnthropicResponseContentBlock {
            Type = AnthropicContentBlockType.WebSearchToolResult,
            Content = JsonElementHelper.FromJson("[]")
        };

        var metadata = AnthropicQueryService.BuildWebSearchResultMetadata(block, "m", "model");

        metadata.Should().NotContainKey("search_links", "空数组无链接不写 search_links");
    }

    [Fact]
    public void BuildWebSearchResultMetadata_NullId_EmptyStringInMetadata() {
        var block = new AnthropicResponseContentBlock {
            Type = AnthropicContentBlockType.WebSearchToolResult,
            Content = null,
            Id = null
        };

        var metadata = AnthropicQueryService.BuildWebSearchResultMetadata(block, "m", "model");

        metadata["tool_use_id"].GetString().Should().Be("");
    }

    #endregion

    #region BuildAnthropicInputSchema — schema 构建

    [Fact]
    public void BuildAnthropicInputSchema_EmptyParameters_ReturnsDefaultInstance() {
        var schema = AnthropicQueryService.BuildAnthropicInputSchema([]);

        schema.Properties.Should().BeEmpty();
        schema.Required.Should().BeEmpty();
    }

    [Fact]
    public void BuildAnthropicInputSchema_SingleParam_MapsNameAndType() {
        var param = new ToolParam("q", "", typeof(string), false);

        var schema = AnthropicQueryService.BuildAnthropicInputSchema([param]);

        schema.Properties.Should().ContainKey("q");
        schema.Properties["q"].Type.Should().Be("string");
        schema.Required.Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(int), "integer")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(string), "string")]
    public void BuildAnthropicInputSchema_VariousTypes_MappedCorrectly(Type clrType, string expected) {
        var schema = AnthropicQueryService.BuildAnthropicInputSchema([new ToolParam("p", "", clrType, false)]);

        schema.Properties["p"].Type.Should().Be(expected);
    }

    [Fact]
    public void BuildAnthropicInputSchema_WithDescription_SetsDescription() {
        var schema = AnthropicQueryService.BuildAnthropicInputSchema([new ToolParam("p", "desc", typeof(string), false)]);

        schema.Properties["p"].Description.Should().Be("desc");
    }

    [Fact]
    public void BuildAnthropicInputSchema_EmptyDescription_DescriptionNull() {
        var schema = AnthropicQueryService.BuildAnthropicInputSchema([new ToolParam("p", "", typeof(string), false)]);

        schema.Properties["p"].Description.Should().BeNull();
    }

    [Fact]
    public void BuildAnthropicInputSchema_RequiredParam_AddedToRequired() {
        var schema = AnthropicQueryService.BuildAnthropicInputSchema([new ToolParam("p", "", typeof(string), true)]);

        schema.Required.Should().ContainSingle().Which.Should().Be("p");
    }

    [Fact]
    public void BuildAnthropicInputSchema_MultipleParams_AllMapped() {
        var parameters = new ToolParam[] {
            new("a", "desc a", typeof(int), true),
            new("b", "", typeof(string), false)
        };

        var schema = AnthropicQueryService.BuildAnthropicInputSchema(parameters);

        schema.Properties.Should().HaveCount(2);
        schema.Required.Should().ContainSingle().Which.Should().Be("a");
    }

    #endregion
}