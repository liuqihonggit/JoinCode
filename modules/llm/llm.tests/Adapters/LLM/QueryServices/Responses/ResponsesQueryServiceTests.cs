// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Llm.Tests.Adapters.LLM.QueryServices.Responses;


public class ResponsesQueryServiceTests {
    private static ResponsesQueryService CreateService(string provider = "openai") {
        var kind = ProtocolKind.OpenAiResponses;
        var config = new ProviderConfig {
            Vendor = provider,
            ApiKey = "sk-test",
            ModelId = "gpt-4o",
            Definition = new FallbackProviderDefinition(kind)
        };
        return new ResponsesQueryService(config);
    }

    #region CreateRequest

    [Fact]
    public void CreateRequest_DefaultSettings_UsesConfigModelId() {
        var service = CreateService();
        var history = new MessageList { new(MessageRole.User, "hi") };

        var request = service.CreateRequest(history, null, stream: false, null);

        request.Model.Should().Be("gpt-4o");
        request.Stream.Should().BeFalse();
        request.Input.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void CreateRequest_FastMode_UsesFastModelId() {
        var service = CreateService();
        var history = new MessageList();
        var options = new ChatOptions {
            FastMode = true,
            FastModelId = "gpt-4o-mini"
        };

        var request = service.CreateRequest(history, options, stream: false, null);

        request.Model.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public void CreateRequest_FastModeWithoutFastModelId_KeepsConfigModel() {
        var service = CreateService();
        var options = new ChatOptions { FastMode = true };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Model.Should().Be("gpt-4o");
    }

    [Fact]
    public void CreateRequest_StreamEnabled_SetsStreamTrue() {
        var service = CreateService();

        var request = service.CreateRequest(new MessageList(), null, stream: true, null);

        request.Stream.Should().BeTrue();
    }

    [Fact]
    public void CreateRequest_SystemMessage_MapsToInstructions() {
        var service = CreateService();
        var history = new MessageList
        {
            new(MessageRole.System, "You are helpful."),
            new(MessageRole.User, "hi")
        };

        var request = service.CreateRequest(history, null, stream: false, null);

        request.Instructions.Should().Be("You are helpful.");
    }

    [Fact]
    public void CreateRequest_MultipleSystemMessages_ConcatenatesInstructions() {
        var service = CreateService();
        var history = new MessageList
        {
            new(MessageRole.System, "Rule 1."),
            new(MessageRole.System, "Rule 2."),
            new(MessageRole.User, "hi")
        };

        var request = service.CreateRequest(history, null, stream: false, null);

        request.Instructions.Should().Be("Rule 1.\nRule 2.");
    }

    [Fact]
    public void CreateRequest_UserMessage_InputArrayContainsUserRole() {
        var service = CreateService();
        var history = new MessageList { new(MessageRole.User, "hello") };

        var request = service.CreateRequest(history, null, stream: false, null);

        request.Input.GetArrayLength().Should().Be(1);
        var item = request.Input[0];
        item.GetProperty("type").GetString().Should().Be("message");
        item.GetProperty("role").GetString().Should().Be("user");
        item.GetProperty("content")[0].GetProperty("type").GetString().Should().Be("input_text");
        item.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("hello");
    }

    [Fact]
    public void CreateRequest_AssistantMessage_UsesOutputTextContentType() {
        var service = CreateService();
        var history = new MessageList
        {
            new(MessageRole.User, "hi"),
            new(MessageRole.Assistant, "hello back")
        };

        var request = service.CreateRequest(history, null, stream: false, null);

        request.Input.GetArrayLength().Should().Be(2);
        var assistantItem = request.Input[1];
        assistantItem.GetProperty("role").GetString().Should().Be("assistant");
        assistantItem.GetProperty("content")[0].GetProperty("type").GetString().Should().Be("output_text");
        assistantItem.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("hello back");
    }

    [Fact]
    public void CreateRequest_TransfersTemperatureTopPMaxOutputTokens() {
        var service = CreateService();
        var options = new ChatOptions {
            Temperature = 0.5f,
            MaxTokens = 100,
            TopP = 0.9f
        };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Temperature.Should().Be(0.5f);
        request.TopP.Should().Be(0.9f);
        request.MaxOutputTokens.Should().Be(100);
    }

    [Fact]
    public void CreateRequest_EffortLevel_SetsReasoningEffort() {
        var service = CreateService();
        var options = new ChatOptions { EffortLevel = EffortLevel.Medium };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Reasoning.Should().NotBeNull();
        request.Reasoning!.Effort.Should().Be("medium");
    }

    [Fact]
    public void CreateRequest_ThinkingEnabled_SetsReasoningWithHighEffort() {
        var service = CreateService();
        var options = new ChatOptions { ThinkingEnabled = true };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Reasoning.Should().NotBeNull();
        request.Reasoning!.Effort.Should().Be("high",
            "ThinkingEnabled 无 EffortLevel 时默认 high effort 开启思考模式");
    }

    [Fact]
    public void CreateRequest_ThinkingDisabled_DoesNotSetReasoning() {
        var service = CreateService();
        var options = new ChatOptions { ThinkingEnabled = false };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Reasoning.Should().BeNull();
    }

    [Fact]
    public async Task CreateRequest_ToolChoiceAutoWithKernel_BuildsTools() {
        var service = CreateService();
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup("tools", [new ToolDef("TestTool", "A test tool")]));
        var options = new ChatOptions { ToolChoice = ToolChoice.AutoInvoke };

        var request = service.CreateRequest(new MessageList(), options, stream: false, kernel);

        request.Tools.Should().NotBeNull();
        request.Tools.Should().ContainSingle();
        request.ToolChoice.Should().Be("auto");
        request.Tools![0].Name.Should().Be("TestTool");
        request.Tools[0].Type.Should().Be("function");
    }

    [Fact]
    public void CreateRequest_ToolChoiceAutoWithoutKernel_DoesNotAddTools() {
        var service = CreateService();
        var options = new ChatOptions { ToolChoice = ToolChoice.AutoInvoke };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Tools.Should().BeEmpty();
    }

    [Fact]
    public void CreateRequest_ToolHistory_EmitsFunctionCallAndFunctionCallOutputItems() {
        // DeepSeek Responses API: 工具调用历史须用 function_call + function_call_output items, 而非 role=tool message
        var service = CreateService();
        var assistantMeta = new Dictionary<string, JsonElement> {
            ["ToolCalls"] = JsonElementHelper.FromJson("[{\"Id\":\"call-1\",\"Name\":\"grep\",\"Arguments\":\"{\\\"q\\\":\\\"x\\\"}\"}]")
        };
        var toolMeta = new Dictionary<string, JsonElement> {
            ["ToolCallId"] = JsonElementHelper.FromString("call-1"),
            ["ToolName"] = JsonElementHelper.FromString("grep")
        };
        var history = new MessageList
        {
            new(MessageRole.User, "search x"),
            new(MessageRole.Assistant, null, assistantMeta),
            new(MessageRole.Tool, "found file", toolMeta)
        };

        var request = service.CreateRequest(history, null, stream: false, null);

        var input = request.Input.GetRawText();
        input.Should().Contain("\"type\":\"function_call\"", "assistant 工具调用应转 function_call item");
        input.Should().Contain("\"call_id\":\"call-1\"");
        input.Should().Contain("\"name\":\"grep\"");
        input.Should().Contain("\"arguments\":\"{\\\"q\\\":\\\"x\\\"}\"");
        input.Should().Contain("\"type\":\"function_call_output\"", "tool 结果应转 function_call_output item");
        input.Should().Contain("\"call_id\":\"call-1\"");
        input.Should().Contain("\"output\":\"found file\"");
        input.Should().NotContain("\"role\":\"tool\"", "禁止使用 Chat Completions 的 role=tool 格式");
    }

    [Fact]
    public void CreateRequest_AssistantWithReasoning_EmitsReasoningItem() {
        // thinking 模式: assistant 的 reasoning 必须以 reasoning item 回传, 否则 DeepSeek 400
        var service = CreateService();
        var assistantMeta = new Dictionary<string, JsonElement> {
            ["ReasoningText"] = JsonElementHelper.FromString("Let me think about this carefully.")
        };
        var history = new MessageList
        {
            new(MessageRole.User, "search x"),
            new(MessageRole.Assistant, "found it", assistantMeta)
        };

        var request = service.CreateRequest(history, null, stream: false, null);

        var input = request.Input.GetRawText();
        input.Should().Contain("\"type\":\"reasoning\"", "assistant 的 reasoning 应回传为 reasoning item");
        input.Should().Contain("\"type\":\"reasoning_text\"");
        input.Should().Contain("\"text\":\"Let me think about this carefully.\"");
    }

    [Fact]
    public void CreateRequest_ToolCallHistoryWithReasoning_EmitsReasoningThenFunctionCallItems() {
        // 工具调用轮: reasoning item 在 function_call 之前回传, 顺序对齐 DeepSeek 输出结构
        var service = CreateService();
        var assistantMeta = new Dictionary<string, JsonElement> {
            ["ReasoningText"] = JsonElementHelper.FromString("I should use grep."),
            ["ToolCalls"] = JsonElementHelper.FromJson("[{\"Id\":\"call-2\",\"Name\":\"grep\",\"Arguments\":\"{}\"}]")
        };
        var toolMeta = new Dictionary<string, JsonElement> {
            ["ToolCallId"] = JsonElementHelper.FromString("call-2"),
            ["ToolName"] = JsonElementHelper.FromString("grep")
        };
        var history = new MessageList
        {
            new(MessageRole.User, "search"),
            new(MessageRole.Assistant, null, assistantMeta),
            new(MessageRole.Tool, "result", toolMeta)
        };

        var request = service.CreateRequest(history, null, stream: false, null);

        var input = request.Input.GetRawText();
        var reasoningIdx = input.IndexOf("\"type\":\"reasoning\"", StringComparison.Ordinal);
        var functionCallIdx = input.IndexOf("\"type\":\"function_call\"", StringComparison.Ordinal);
        reasoningIdx.Should().BeGreaterThan(-1);
        functionCallIdx.Should().BeGreaterThan(reasoningIdx, "reasoning item 应在 function_call 之前");
    }

    [Fact]
    public void CreateRequest_SpecialCharactersInContent_EscapedProperly() {
        var service = CreateService();
        var history = new MessageList { new(MessageRole.User, "hello \"world\"\nnewline") };

        var request = service.CreateRequest(history, null, stream: false, null);

        var text = request.Input[0].GetProperty("content")[0].GetProperty("text").GetString();
        text.Should().Be("hello \"world\"\nnewline");
    }

    #endregion

    #region ConvertToApiMessages

    [Fact]
    public async Task Stream_ReasoningDelta_AccumulatedInFinalMetadata() {
        // thinking 模式下流式 reasoning_text.delta 应累积到终局事件的 ReasoningText metadata
        var sse =
            "event: response.reasoning_text.delta\ndata: {\"delta\":\"Let me think\"}\n\n" +
            "event: response.reasoning_text.delta\ndata: {\"delta\":\" about this.\"}\n\n" +
            "event: response.output_text.delta\ndata: {\"delta\":\"Answer\"}\n\n" +
            "event: response.completed\ndata: {\"response\":{\"id\":\"resp-9\",\"status\":\"completed\",\"usage\":{\"input_tokens\":10,\"output_tokens\":20}}}\n\n";
        var service = CreateStreamingService(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var events = new List<StreamEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evt in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token)) {
            events.Add(evt);
        }

        var final = events[^1];
        final.Metadata!.Should().ContainKey("ReasoningText", "流式 reasoning 应累积到终局 metadata");
        final.Metadata!["ReasoningText"].GetString().Should().Be("Let me think about this.");
    }

    [Fact]
    public void ConvertToApiMessages_ReasoningItem_StoredInAssistantMetadata() {
        // DeepSeek thinking 模式下响应含 reasoning item，必须存入 metadata 供下轮回传
        var response = new ResponsesResponse {
            Id = "resp-r1",
            Output =
            [
                new ResponsesOutputItem
                {
                    Type = "reasoning",
                    Content =
                    [
                        new ResponsesContent { Type = "reasoning_text", Text = "I need to search for the file." },
                        new ResponsesContent { Type = "reasoning_text", Text = "Let me use grep." }
                    ]
                },
                new ResponsesOutputItem
                {
                    Type = "message",
                    Role = "assistant",
                    Content = [new ResponsesContent { Type = "output_text", Text = "Found it." }]
                }
            ]
        };

        var messages = ResponsesQueryService.ConvertToApiMessages(response);

        messages.Should().ContainSingle();
        messages[0].Content.Should().Be("Found it.");
        messages[0].Metadata!.Should().ContainKey("ReasoningText", "reasoning 内容应存入 assistant 消息 metadata");
        messages[0].Metadata!["ReasoningText"].GetString().Should().Be("I need to search for the file.Let me use grep.");
    }

    [Fact]
    public void ConvertToApiMessages_NoReasoning_NoReasoningTextKey() {
        var response = new ResponsesResponse {
            Id = "resp-r2",
            Output =
            [
                new ResponsesOutputItem
                {
                    Type = "message",
                    Role = "assistant",
                    Content = [new ResponsesContent { Type = "output_text", Text = "Plain" }]
                }
            ]
        };

        var messages = ResponsesQueryService.ConvertToApiMessages(response);

        messages[0].Metadata!.Should().NotContainKey("ReasoningText");
    }

    [Fact]
    public void ConvertToApiMessages_ReasoningWithToolCalls_StoredInToolCallsMetadata() {
        var response = new ResponsesResponse {
            Id = "resp-r3",
            Output =
            [
                new ResponsesOutputItem
                {
                    Type = "reasoning",
                    Content = [new ResponsesContent { Type = "reasoning_text", Text = "thinking about it" }]
                },
                new ResponsesOutputItem
                {
                    Type = "function_call",
                    CallId = "call-9",
                    Name = "grep",
                    Arguments = "{\"q\":\"x\"}"
                }
            ]
        };

        var messages = ResponsesQueryService.ConvertToApiMessages(response);

        messages.Should().ContainSingle();
        messages[0].Metadata!.Should().ContainKey("AllToolCalls");
        messages[0].Metadata!["ReasoningText"].GetString().Should().Be("thinking about it");
    }

    [Fact]
    public void ConvertToApiMessages_TextResponse_ReturnsAssistantMessageWithText() {
        var response = new ResponsesResponse {
            Id = "resp-1",
            Object = "response",
            Model = "gpt-4o",
            Status = "completed",
            Output =
            [
                new ResponsesOutputItem
                {
                    Type = "message",
                    Role = "assistant",
                    Content = [new ResponsesContent { Type = "output_text", Text = "Hello!" }]
                }
            ]
        };

        var messages = ResponsesQueryService.ConvertToApiMessages(response);

        messages.Should().ContainSingle();
        messages[0].Role.Should().Be(MessageRole.Assistant);
        messages[0].Content.Should().Be("Hello!");
        messages[0].Metadata!.Should().ContainKey("Id");
        messages[0].Metadata!["Id"].GetString().Should().Be("resp-1");
        messages[0].Metadata!["FinishReason"].GetString().Should().Be("completed");
    }

    [Fact]
    public void ConvertToApiMessages_MultipleContentTexts_Concatenated() {
        var response = new ResponsesResponse {
            Id = "resp-2",
            Output =
            [
                new ResponsesOutputItem
                {
                    Type = "message",
                    Role = "assistant",
                    Content =
                    [
                        new ResponsesContent { Type = "output_text", Text = "Part 1 " },
                        new ResponsesContent { Type = "output_text", Text = "Part 2" }
                    ]
                }
            ]
        };

        var messages = ResponsesQueryService.ConvertToApiMessages(response);

        messages[0].Content.Should().Be("Part 1 Part 2");
    }

    [Fact]
    public void ConvertToApiMessages_FunctionCall_ReturnsToolCallsMetadata() {
        var response = new ResponsesResponse {
            Id = "resp-3",
            Output =
            [
                new ResponsesOutputItem
                {
                    Type = "function_call",
                    CallId = "call-1",
                    Name = "get_weather",
                    Arguments = "{\"city\":\"SF\"}"
                }
            ]
        };

        var messages = ResponsesQueryService.ConvertToApiMessages(response);

        messages.Should().ContainSingle();
        messages[0].Role.Should().Be(MessageRole.Assistant);
        messages[0].Content.Should().BeNull();
        messages[0].Metadata!.Should().ContainKey("AllToolCalls");
        messages[0].Metadata!["FinishReason"].GetString().Should().Be("tool_calls");
    }

    [Fact]
    public void ConvertToApiMessages_Usage_MapsToTokenUsage() {
        var response = new ResponsesResponse {
            Id = "resp-4",
            Output =
            [
                new ResponsesOutputItem
                {
                    Type = "message",
                    Role = "assistant",
                    Content = [new ResponsesContent { Type = "output_text", Text = "Hi" }]
                }
            ],
            Usage = new ResponsesUsage {
                InputTokens = 10,
                OutputTokens = 20,
                InputTokensDetails = new ResponsesTokenDetails { CachedTokens = 5 }
            }
        };

        var messages = ResponsesQueryService.ConvertToApiMessages(response);

        messages[0].Metadata!.Should().ContainKey("Usage");
        var usage = messages[0].Metadata!["Usage"].Deserialize<TokenUsage>(NativeJsonContext.Default.TokenUsage);
        usage.Should().NotBeNull();
        usage!.PromptTokens.Should().Be(10);
        usage.CompletionTokens.Should().Be(20);
        usage.CacheReadInputTokens.Should().Be(5);
    }

    [Fact]
    public void ConvertToApiMessages_EmptyOutput_ReturnsEmptyContentMessage() {
        var response = new ResponsesResponse {
            Id = "resp-5",
            Output = []
        };

        var messages = ResponsesQueryService.ConvertToApiMessages(response);

        messages.Should().ContainSingle();
        messages[0].Content.Should().Be("");
    }

    [Fact]
    public void ConvertToApiMessages_TextAndFunctionCall_PrioritizesToolCalls() {
        var response = new ResponsesResponse {
            Id = "resp-6",
            Output =
            [
                new ResponsesOutputItem
                {
                    Type = "message",
                    Role = "assistant",
                    Content = [new ResponsesContent { Type = "output_text", Text = "Let me check." }]
                },
                new ResponsesOutputItem
                {
                    Type = "function_call",
                    CallId = "call-2",
                    Name = "search",
                    Arguments = "{}"
                }
            ]
        };

        var messages = ResponsesQueryService.ConvertToApiMessages(response);

        messages.Should().ContainSingle();
        messages[0].Content.Should().BeNull("有 tool_calls 时 content 置 null");
        messages[0].Metadata!.Should().ContainKey("AllToolCalls");
        messages[0].Metadata!["FinishReason"].GetString().Should().Be("tool_calls");
    }

    #endregion

    #region 流式事件解析

    /// <summary>用 mock SSE 流构建服务 — 验证流式终结事件解析</summary>
    private static ResponsesQueryService CreateStreamingService(string sseBody) {
        var kind = ProtocolKind.OpenAiResponses;
        var config = new ProviderConfig {
            Vendor = "openai",
            ApiKey = "sk-test",
            ModelId = "gpt-4o",
            Definition = new FallbackProviderDefinition(kind)
        };

        var handler = new MockResponsesStreamingHandler(sseBody);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9901/") };
        return new ResponsesQueryService(config, httpClient);
    }

    [Fact]
    public async Task Stream_IncompleteEvent_YieldsFinalMetadataWithUsage() {
        // 官方协议: 终结事件有三个(completed/incomplete/failed)，incomplete 出现在 max_output_tokens 截断
        var sse =
            "event: response.output_text.delta\ndata: {\"delta\":\"Hello\"}\n\n" +
            "event: response.incomplete\ndata: {\"response\":{\"id\":\"resp-1\",\"status\":\"incomplete\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"},\"usage\":{\"input_tokens\":10,\"output_tokens\":20}}}\n\n";
        var service = CreateStreamingService(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var events = new List<StreamEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evt in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token)) {
            events.Add(evt);
        }

        events.Should().NotBeEmpty("应收到文本增量事件");
        var final = events[^1];
        final.Content.Should().Be("");
        final.Metadata.Should().ContainKey("Usage", "incomplete 事件应携带 usage 收尾");
        var usage = final.Metadata!["Usage"].Deserialize<TokenUsage>(NativeJsonContext.Default.TokenUsage);
        usage.Should().NotBeNull();
        usage!.PromptTokens.Should().Be(10);
        usage.CompletionTokens.Should().Be(20);
    }

    [Fact]
    public async Task Stream_IncompleteEvent_WithToolCalls_FlushesAccumulatedToolCalls() {
        // incomplete 截断时已累积的 function_call arguments 应被刷出，避免丢失
        var sse =
            "event: response.output_item.added\ndata: {\"item\":{\"type\":\"function_call\",\"id\":\"fc_1\",\"call_id\":\"call-1\",\"name\":\"get_weather\",\"arguments\":\"\"}}\n\n" +
            "event: response.function_call_arguments.delta\ndata: {\"item_id\":\"fc_1\",\"delta\":\"{\\\"city\\\":\\\"SF\\\"}\"}\n\n" +
            "event: response.incomplete\ndata: {\"response\":{\"id\":\"resp-2\",\"status\":\"incomplete\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"},\"usage\":{\"input_tokens\":10,\"output_tokens\":20}}}\n\n";
        var service = CreateStreamingService(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var events = new List<StreamEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evt in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token)) {
            events.Add(evt);
        }

        var final = events[^1];
        final.Metadata.Should().ContainKey("AllToolCalls", "incomplete 截断时应刷出已累积的工具调用");
        final.Metadata!["FinishReason"].GetString().Should().Be("tool_calls");
        final.Metadata!["AllToolCalls"].GetRawText().Should().Contain("get_weather");
    }

    [Fact]
    public async Task Stream_DataWithoutEventPrefix_UsesTypeFieldFallback() {
        // 某些服务端/网关只发 data: 不带 event: 前缀，事件类型在 data 的 type 字段中
        var sse =
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"Hello\"}\n\n" +
            "data: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp-1\",\"status\":\"completed\",\"usage\":{\"input_tokens\":10,\"output_tokens\":20}}}\n\n";
        var service = CreateStreamingService(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var events = new List<StreamEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evt in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token)) {
            events.Add(evt);
        }

        events.Should().NotBeEmpty("无 event 前缀时应通过 data.type 容错解析");
        events[0].Content.Should().Be("Hello");
        events[^1].Metadata.Should().ContainKey("Usage", "无前缀时 completed 事件应正常收尾");
    }

    private sealed class MockResponsesStreamingHandler : DelegatingHandler {
        private readonly string _sseBody;

        public MockResponsesStreamingHandler(string sseBody) : base(new HttpClientHandler()) {
            _sseBody = sseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var content = new StringContent(_sseBody, Encoding.UTF8, "text/event-stream");
            var response = new HttpResponseMessage(HttpStatusCode.OK) {
                Content = content
            };
            return Task.FromResult(response);
        }
    }

    #endregion

    #region Two-Phase Tool Loading — BuildToolsFromKernel

    [Fact]
    public async Task BuildToolsFromKernel_OnlyCoreTools_ToolsPopulated_ToolGroupsEmpty() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.CoreTools, [
            new ToolDef("read", "Read a file"),
            new ToolDef("write", "Write a file")
        ]));

        var (tools, toolGroups) = ResponsesQueryService.BuildToolsFromKernel(kernel);

        tools.Should().HaveCount(2);
        tools.Select(t => t.Name).Should().Contain(["read", "write"]);
        toolGroups.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildToolsFromKernel_OnlyMcpTools_ToolsEmpty_ToolGroupsPopulated() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.server1.tool1", "MCP tool 1"),
            new ToolDef("mcp.server2.tool2", "MCP tool 2")
        ]));

        var (tools, toolGroups) = ResponsesQueryService.BuildToolsFromKernel(kernel);

        tools.Should().BeEmpty();
        toolGroups.Should().ContainSingle();
        toolGroups[0].Name.Should().Be(ToolGroupNameConstants.McpTools);
        toolGroups[0].Tools.Should().Contain(["mcp.server1.tool1", "mcp.server2.tool2"]);
    }

    [Fact]
    public async Task BuildToolsFromKernel_MixedTools_CoreToolsInTools_McpToolsInGroups() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.CoreTools, [
            new ToolDef("read", "Read a file")
        ]));
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1")
        ]));

        var (tools, toolGroups) = ResponsesQueryService.BuildToolsFromKernel(kernel);

        tools.Should().ContainSingle();
        tools[0].Name.Should().Be("read");
        toolGroups.Should().ContainSingle();
        toolGroups[0].Name.Should().Be(ToolGroupNameConstants.McpTools);
        toolGroups[0].Tools.Should().ContainSingle().Which.Should().Be("mcp.tool1");
    }

    #endregion

    #region Two-Phase Tool Loading — CreateSecondResponsesRequestWithDescriptions

    [Fact]
    public async Task CreateSecondResponsesRequestWithDescriptions_ValidToolNames_BuildsDescriptions() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1"),
            new ToolDef("mcp.tool2", "MCP tool 2")
        ]));

        var originalRequest = new ResponsesRequest {
            Model = "gpt-4o",
            Stream = true
        };
        var descRequestContent = """{"tools":["mcp.tool1","mcp.tool2"]}""";

        var secondRequest = ResponsesQueryService.CreateSecondResponsesRequestWithDescriptions(originalRequest, descRequestContent, kernel);

        secondRequest.ToolDescriptions.Should().HaveCount(2);
        secondRequest.ToolDescriptions!.Select(t => t.Name).Should().Contain(["mcp.tool1", "mcp.tool2"]);
        secondRequest.Model.Should().Be("gpt-4o");
        secondRequest.Stream.Should().BeTrue();
    }

    [Fact]
    public async Task CreateSecondResponsesRequestWithDescriptions_UnknownToolNames_DescriptionsEmpty() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1")
        ]));

        var originalRequest = new ResponsesRequest { Model = "gpt-4o" };
        var descRequestContent = """{"tools":["nonexistent.tool"]}""";

        var secondRequest = ResponsesQueryService.CreateSecondResponsesRequestWithDescriptions(originalRequest, descRequestContent, kernel);

        secondRequest.ToolDescriptions.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateSecondResponsesRequestWithDescriptions_PreservesOriginalFields() {
        await using var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1")
        ]));

        var originalRequest = new ResponsesRequest {
            Model = "gpt-4o",
            Stream = true,
            Temperature = 0.7f,
            MaxOutputTokens = 1000,
            Tools = [new ResponsesTool { Type = "function", Name = "read" }],
            ToolGroups = [new OpenAIToolGroup { Name = "mcp_tools", Tools = ["mcp.tool1"] }]
        };
        var descRequestContent = """{"tools":["mcp.tool1"]}""";

        var secondRequest = ResponsesQueryService.CreateSecondResponsesRequestWithDescriptions(originalRequest, descRequestContent, kernel);

        secondRequest.Model.Should().Be("gpt-4o");
        secondRequest.Temperature.Should().Be(0.7f);
        secondRequest.MaxOutputTokens.Should().Be(1000);
        secondRequest.Tools.Should().BeSameAs(originalRequest.Tools);
        secondRequest.ToolGroups.Should().BeSameAs(originalRequest.ToolGroups);
        secondRequest.ToolDescriptions.Should().ContainSingle();
    }

    #endregion

    #region BuildFunctionCallOutput — function_call_output 构建

    [Fact]
    public void BuildFunctionCallOutput_WithCallId_EmitsFunctionCallOutputItem() {
        var msg = new ApiMessage(MessageRole.Tool, "result body",
            new Dictionary<string, JsonElement> {
                [MessageMetadataKeyEnumConstants.ToolCallId] = JsonElementHelper.FromString("call-1")
            });

        var dto = ResponsesQueryService.BuildFunctionCallOutput(msg);
        var json = JsonSerializer.Serialize(dto, NativeJsonContext.Default.ResponsesInputItemDto);

        json.Should().Contain("\"type\":\"function_call_output\"");
        json.Should().Contain("\"call_id\":\"call-1\"");
        json.Should().Contain("\"output\":\"result body\"");
    }

    [Fact]
    public void BuildFunctionCallOutput_NoMetadata_CallIdIsEmpty() {
        var msg = new ApiMessage(MessageRole.Tool, "x");

        var dto = ResponsesQueryService.BuildFunctionCallOutput(msg);
        var json = JsonSerializer.Serialize(dto, NativeJsonContext.Default.ResponsesInputItemDto);

        json.Should().Contain("\"call_id\":\"\"");
    }

    #endregion

    #region TryBuildAssistantMetadata — reasoning + tool_calls 分支

    [Fact]
    public void TryBuildAssistantMetadata_NonAssistant_ReturnsNull() {
        var msg = new ApiMessage(MessageRole.User, "hi");
        ResponsesQueryService.TryBuildAssistantMetadata(msg).Should().BeNull();
    }

    [Fact]
    public void TryBuildAssistantMetadata_AssistantWithoutMetadata_ReturnsNull() {
        var msg = new ApiMessage(MessageRole.Assistant, "hi");
        ResponsesQueryService.TryBuildAssistantMetadata(msg).Should().BeNull();
    }

    [Fact]
    public void TryBuildAssistantMetadata_WithReasoning_EmitsReasoningItem() {
        var msg = new ApiMessage(MessageRole.Assistant, null,
            new Dictionary<string, JsonElement> {
                [MessageMetadataKeyEnumConstants.ReasoningText] = JsonElementHelper.FromString("think hard")
            });

        var items = ResponsesQueryService.TryBuildAssistantMetadata(msg);
        items.Should().NotBeNull();
        var json = JsonSerializer.Serialize(items, NativeJsonContext.Default.ListResponsesInputItemDto);
        json.Should().Contain("\"type\":\"reasoning\"");
        json.Should().Contain("\"text\":\"think hard\"");
    }

    [Fact]
    public void TryBuildAssistantMetadata_WithToolCallsArray_EmitsFunctionCalls() {
        var toolCallsJson = "[{\"Id\":\"call-1\",\"Name\":\"grep\",\"Arguments\":\"{\\\"q\\\":\\\"x\\\"}\"}]";
        var msg = new ApiMessage(MessageRole.Assistant, null,
            new Dictionary<string, JsonElement> {
                [MessageMetadataKeyEnumConstants.ToolCalls] = JsonElementHelper.FromJson(toolCallsJson)
            });

        var items = ResponsesQueryService.TryBuildAssistantMetadata(msg);
        items.Should().NotBeNull();
        var json = JsonSerializer.Serialize(items, NativeJsonContext.Default.ListResponsesInputItemDto);
        json.Should().Contain("\"type\":\"function_call\"");
        json.Should().Contain("\"call_id\":\"call-1\"");
        json.Should().Contain("\"name\":\"grep\"");
    }

    [Fact]
    public void TryBuildAssistantMetadata_WithAllToolCallsArray_EmitsFunctionCalls() {
        var toolCallsJson = "[{\"Id\":\"c2\",\"Name\":\"ls\",\"Arguments\":\"{}\"}]";
        var msg = new ApiMessage(MessageRole.Assistant, null,
            new Dictionary<string, JsonElement> {
                ["AllToolCalls"] = JsonElementHelper.FromJson(toolCallsJson)
            });

        var items = ResponsesQueryService.TryBuildAssistantMetadata(msg);
        items.Should().NotBeNull();
        var json = JsonSerializer.Serialize(items, NativeJsonContext.Default.ListResponsesInputItemDto);
        json.Should().Contain("\"name\":\"ls\"");
    }

    [Fact]
    public void TryBuildAssistantMetadata_ToolCallsNotArray_ReturnsNull() {
        var msg = new ApiMessage(MessageRole.Assistant, null,
            new Dictionary<string, JsonElement> {
                [MessageMetadataKeyEnumConstants.ToolCalls] = JsonElementHelper.FromString("not-array")
            });

        ResponsesQueryService.TryBuildAssistantMetadata(msg).Should().BeNull();
    }

    #endregion

    #region BuildParameters — 手写 JSON schema

    [Fact]
    public void BuildParameters_EmptyParameters_ReturnsNull() {
        ResponsesQueryService.BuildParameters([]).Should().BeNull();
    }

    [Fact]
    public void BuildParameters_SingleParamNoDescription_EmitsTypeOnly() {
        var param = new ToolParam("name", "", typeof(string), false);

        var result = ResponsesQueryService.BuildParameters([param]);

        result.Should().NotBeNull();
        var json = result!.Value.GetRawText();
        json.Should().Contain("\"type\":\"object\"");
        json.Should().Contain("\"name\":{\"type\":\"string\"}");
        json.Should().NotContain("\"description\"");
        json.Should().NotContain("\"required\"");
    }

    [Fact]
    public void BuildParameters_WithDescription_EmitsDescriptionField() {
        var param = new ToolParam("q", "query text", typeof(string), false);

        var result = ResponsesQueryService.BuildParameters([param]);

        var json = result!.Value.GetRawText();
        json.Should().Contain("\"description\":\"query text\"");
    }

    [Fact]
    public void BuildParameters_RequiredParam_EmitsRequiredArray() {
        var param = new ToolParam("q", "", typeof(string), true);

        var result = ResponsesQueryService.BuildParameters([param]);

        var json = result!.Value.GetRawText();
        json.Should().Contain("\"required\":[\"q\"]");
    }

    [Fact]
    public void BuildParameters_MultipleParams_CommaSeparated() {
        var parameters = new ToolParam[] {
            new("a", "", typeof(string), false),
            new("b", "", typeof(int), true)
        };

        var result = ResponsesQueryService.BuildParameters(parameters);

        var json = result!.Value.GetRawText();
        json.Should().Contain("\"a\":{\"type\":\"string\"}");
        json.Should().Contain("\"b\":{\"type\":\"integer\"}");
        json.Should().Contain("\"required\":[\"b\"]");
    }

    #endregion

    #region BuildTokenUsage — token 映射

    [Fact]
    public void BuildTokenUsage_FromResponsesUsage_MapsAllFields() {
        var usage = new ResponsesUsage {
            InputTokens = 10,
            OutputTokens = 20,
            InputTokensDetails = new ResponsesTokenDetails { CachedTokens = 5 }
        };

        var result = ResponsesQueryService.BuildTokenUsage(usage);

        result.PromptTokens.Should().Be(10);
        result.CompletionTokens.Should().Be(20);
        result.CacheReadInputTokens.Should().Be(5);
        result.CacheCreationInputTokens.Should().Be(0);
    }

    [Fact]
    public void BuildTokenUsage_FromResponsesUsage_NullDetails_CacheReadZero() {
        var usage = new ResponsesUsage { InputTokens = 1, OutputTokens = 2 };

        var result = ResponsesQueryService.BuildTokenUsage(usage);

        result.CacheReadInputTokens.Should().Be(0);
    }

    [Fact]
    public void BuildTokenUsage_FromJsonElement_MapsAllFields() {
        var json = JsonElementHelper.FromJson("""
            {"input_tokens":100,"output_tokens":200,"input_tokens_details":{"cached_tokens":50}}
            """);

        var result = ResponsesQueryService.BuildTokenUsage(json);

        result.PromptTokens.Should().Be(100);
        result.CompletionTokens.Should().Be(200);
        result.CacheReadInputTokens.Should().Be(50);
        result.CacheCreationInputTokens.Should().Be(0);
    }

    [Fact]
    public void BuildTokenUsage_FromJsonElement_MissingFields_DefaultToZero() {
        var json = JsonElementHelper.FromJson("{}");

        var result = ResponsesQueryService.BuildTokenUsage(json);

        result.PromptTokens.Should().Be(0);
        result.CompletionTokens.Should().Be(0);
        result.CacheReadInputTokens.Should().Be(0);
    }

    #endregion

    #region IsToolDescriptionRequestComplete — tool_description_request 检测

    [Fact]
    public void IsToolDescriptionRequestComplete_NoMarker_ReturnsFalse() {
        ResponsesQueryService.IsToolDescriptionRequestComplete("just regular text").Should().BeFalse();
    }

    [Fact]
    public void IsToolDescriptionRequestComplete_MarkerButNoClosingBrace_ReturnsFalse() {
        ResponsesQueryService.IsToolDescriptionRequestComplete("""{"tool_description_request":"partial""").Should().BeFalse();
    }

    [Fact]
    public void IsToolDescriptionRequestComplete_MarkerWithClosingBrace_ReturnsTrue() {
        ResponsesQueryService.IsToolDescriptionRequestComplete("""{"tool_description_request":true}""").Should().BeTrue();
    }

    [Fact]
    public void IsToolDescriptionRequestComplete_ClosingBraceWithTrailingWhitespace_ReturnsTrue() {
        ResponsesQueryService.IsToolDescriptionRequestComplete("""{"tool_description_request":true}   """).Should().BeTrue();
    }

    [Fact]
    public void IsToolDescriptionRequestComplete_EmptyString_ReturnsFalse() {
        ResponsesQueryService.IsToolDescriptionRequestComplete("").Should().BeFalse();
    }

    #endregion

    #region GetItemIdIndex — item_id 哈希索引

    [Fact]
    public void GetItemIdIndex_AlwaysNonNegative() {
        var idx = ResponsesQueryService.GetItemIdIndex("some_item_id");
        idx.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetItemIdIndex_SameInput_ProducesSameIndex() {
        var idx1 = ResponsesQueryService.GetItemIdIndex("fc_123");
        var idx2 = ResponsesQueryService.GetItemIdIndex("fc_123");
        idx1.Should().Be(idx2);
    }

    [Fact]
    public void GetItemIdIndex_DifferentInput_MayProduceDifferentIndex() {
        var idx1 = ResponsesQueryService.GetItemIdIndex("fc_1");
        var idx2 = ResponsesQueryService.GetItemIdIndex("fc_2");
        (idx1 != idx2).Should().BeTrue("不同输入通常产生不同索引");
    }

    [Fact]
    public void GetItemIdIndex_EmptyString_NonNegative() {
        ResponsesQueryService.GetItemIdIndex("").Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region ParseFunctionCallItem — function_call item 解析

    [Fact]
    public void ParseFunctionCallItem_ValidItem_ReturnsCallIdNameIdx() {
        var item = JsonElementHelper.FromJson("""{"type":"function_call","call_id":"call-1","name":"get_weather","id":"fc_1"}""");

        var (callId, name, idx) = ResponsesQueryService.ParseFunctionCallItem(item);

        callId.Should().Be("call-1");
        name.Should().Be("get_weather");
        idx.Should().Be(ResponsesQueryService.GetItemIdIndex("fc_1"));
    }

    [Fact]
    public void ParseFunctionCallItem_MissingCallId_ReturnsEmpty() {
        var item = JsonElementHelper.FromJson("""{"type":"function_call","name":"tool","id":"fc_1"}""");

        var (callId, name, _) = ResponsesQueryService.ParseFunctionCallItem(item);

        callId.Should().Be("");
        name.Should().Be("tool");
    }

    [Fact]
    public void ParseFunctionCallItem_MissingName_ReturnsEmpty() {
        var item = JsonElementHelper.FromJson("""{"type":"function_call","call_id":"c1","id":"fc_1"}""");

        var (callId, name, _) = ResponsesQueryService.ParseFunctionCallItem(item);

        callId.Should().Be("c1");
        name.Should().Be("");
    }

    [Fact]
    public void ParseFunctionCallItem_MissingId_IdxFromEmptyString() {
        var item = JsonElementHelper.FromJson("""{"type":"function_call","call_id":"c1","name":"tool"}""");

        var (_, _, idx) = ResponsesQueryService.ParseFunctionCallItem(item);

        idx.Should().Be(ResponsesQueryService.GetItemIdIndex(""));
    }

    [Fact]
    public void ParseFunctionCallItem_NullStringValues_ReturnsEmptyStrings() {
        var item = JsonElementHelper.FromJson("""{"type":"function_call","call_id":null,"name":null,"id":null}""");

        var (callId, name, _) = ResponsesQueryService.ParseFunctionCallItem(item);

        callId.Should().Be("");
        name.Should().Be("");
    }

    #endregion

    #region BuildToolCallEntriesFromAccumulator — 累积器转 ToolCallEntry

    [Fact]
    public void BuildToolCallEntriesFromAccumulator_EmptyAccumulator_ReturnsEmptyList() {
        var accumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();

        var entries = ResponsesQueryService.BuildToolCallEntriesFromAccumulator(accumulator);

        entries.Should().BeEmpty();
    }

    [Fact]
    public void BuildToolCallEntriesFromAccumulator_SingleEntry_MapsAllFields() {
        var accumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)> {
            [1] = ("call-1", "get_weather", new StringBuilder("{\"city\":\"SF\"}"))
        };

        var entries = ResponsesQueryService.BuildToolCallEntriesFromAccumulator(accumulator);

        entries.Should().ContainSingle();
        entries[0].Id.Should().Be("call-1");
        entries[0].Name.Should().Be("get_weather");
        entries[0].Arguments.Should().Be("""{"city":"SF"}""");
    }

    [Fact]
    public void BuildToolCallEntriesFromAccumulator_MultipleEntries_AllMapped() {
        var accumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)> {
            [1] = ("call-1", "tool_a", new StringBuilder("{}")),
            [2] = ("call-2", "tool_b", new StringBuilder("{\"x\":1}"))
        };

        var entries = ResponsesQueryService.BuildToolCallEntriesFromAccumulator(accumulator);

        entries.Should().HaveCount(2);
        entries.Select(e => e.Name).Should().Contain(["tool_a", "tool_b"]);
    }

    [Fact]
    public void BuildToolCallEntriesFromAccumulator_EmptyArguments_ReturnsEmptyString() {
        var accumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)> {
            [1] = ("call-1", "tool", new StringBuilder())
        };

        var entries = ResponsesQueryService.BuildToolCallEntriesFromAccumulator(accumulator);

        entries[0].Arguments.Should().Be("");
    }

    #endregion

    #region PopulateCompletedMetadata — 终局事件 metadata 填充

    [Fact]
    public void PopulateCompletedMetadata_WithUsage_AddsFinishReasonAndUsage() {
        var metadata = new Dictionary<string, JsonElement>();
        var eventJson = JsonElementHelper.FromJson("""{"response":{"usage":{"input_tokens":10,"output_tokens":20}}}""");
        var toolAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();

        ResponsesQueryService.PopulateCompletedMetadata(metadata, eventJson, toolAccumulator, null);

        metadata.Should().ContainKey("FinishReason");
        metadata["FinishReason"].GetString().Should().Be("stop");
        metadata.Should().ContainKey("Usage");
    }

    [Fact]
    public void PopulateCompletedMetadata_WithToolCalls_OverridesFinishReasonToToolCalls() {
        var metadata = new Dictionary<string, JsonElement>();
        var eventJson = JsonElementHelper.FromJson("""{"response":{"usage":{"input_tokens":10,"output_tokens":20}}}""");
        var toolAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)> {
            [1] = ("call-1", "get_weather", new StringBuilder("{\"city\":\"SF\"}"))
        };

        ResponsesQueryService.PopulateCompletedMetadata(metadata, eventJson, toolAccumulator, null);

        metadata["FinishReason"].GetString().Should().Be("tool_calls");
        metadata.Should().ContainKey("AllToolCalls");
        metadata["AllToolCalls"].GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void PopulateCompletedMetadata_WithReasoning_AddsReasoningText() {
        var metadata = new Dictionary<string, JsonElement>();
        var eventJson = JsonElementHelper.FromJson("""{}""");
        var toolAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();
        var reasoning = new StringBuilder("Let me think about this.");

        ResponsesQueryService.PopulateCompletedMetadata(metadata, eventJson, toolAccumulator, reasoning);

        metadata.Should().ContainKey("ReasoningText");
        metadata["ReasoningText"].GetString().Should().Be("Let me think about this.");
    }

    [Fact]
    public void PopulateCompletedMetadata_NullReasoning_DoesNotAddReasoningText() {
        var metadata = new Dictionary<string, JsonElement>();
        var eventJson = JsonElementHelper.FromJson("""{}""");
        var toolAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();

        ResponsesQueryService.PopulateCompletedMetadata(metadata, eventJson, toolAccumulator, null);

        metadata.Should().NotContainKey("ReasoningText");
    }

    [Fact]
    public void PopulateCompletedMetadata_EmptyReasoning_DoesNotAddReasoningText() {
        var metadata = new Dictionary<string, JsonElement>();
        var eventJson = JsonElementHelper.FromJson("""{}""");
        var toolAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();

        ResponsesQueryService.PopulateCompletedMetadata(metadata, eventJson, toolAccumulator, new StringBuilder());

        metadata.Should().NotContainKey("ReasoningText");
    }

    [Fact]
    public void PopulateCompletedMetadata_NoUsage_NoFinishReasonFromUsage() {
        var metadata = new Dictionary<string, JsonElement>();
        var eventJson = JsonElementHelper.FromJson("""{}""");
        var toolAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();

        ResponsesQueryService.PopulateCompletedMetadata(metadata, eventJson, toolAccumulator, null);

        metadata.Should().NotContainKey("FinishReason");
        metadata.Should().NotContainKey("Usage");
    }

    #endregion

    #region ExtractFailedErrorMessage — 失败事件错误提取

    [Fact]
    public void ExtractFailedErrorMessage_WithErrorObject_ReturnsRawText() {
        var eventJson = JsonElementHelper.FromJson("""{"response":{"error":{"code":"rate_limited","message":"Too many requests"}}}""");

        var error = ResponsesQueryService.ExtractFailedErrorMessage(eventJson);

        error.Should().Contain("rate_limited");
        error.Should().Contain("Too many requests");
    }

    [Fact]
    public void ExtractFailedErrorMessage_NoErrorField_ReturnsUnknown() {
        var eventJson = JsonElementHelper.FromJson("""{"response":{}}""");

        var error = ResponsesQueryService.ExtractFailedErrorMessage(eventJson);

        error.Should().Be("unknown error");
    }

    [Fact]
    public void ExtractFailedErrorMessage_NoResponseField_ReturnsUnknown() {
        var eventJson = JsonElementHelper.FromJson("""{}""");

        var error = ResponsesQueryService.ExtractFailedErrorMessage(eventJson);

        error.Should().Be("unknown error");
    }

    [Fact]
    public void ExtractFailedErrorMessage_StringError_ReturnsQuotedString() {
        var eventJson = JsonElementHelper.FromJson("""{"response":{"error":"bad request"}}""");

        var error = ResponsesQueryService.ExtractFailedErrorMessage(eventJson);

        error.Should().Contain("bad request");
    }

    #endregion
}