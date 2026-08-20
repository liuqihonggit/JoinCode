namespace Llm.Tests.Adapters.LLM.QueryServices.Responses;

using System.Text.Json;
using Api.LLM.QueryServices;
using Api.LLM.QueryServices.Responses;

public class ResponsesQueryServiceTests
{
    private static ResponsesQueryService CreateService(string provider = "openai")
    {
        var kind = ProtocolKind.OpenAiResponses;
        var config = new ProviderConfig
        {
            Vendor = provider,
            ApiKey = "sk-test",
            ModelId = "gpt-4o",
            Definition = new FallbackProviderDefinition(kind)
        };
        return new ResponsesQueryService(config);
    }

    #region CreateRequest

    [Fact]
    public void CreateRequest_DefaultSettings_UsesConfigModelId()
    {
        var service = CreateService();
        var history = new MessageList { new(MessageRole.User, "hi") };

        var request = service.CreateRequest(history, null, stream: false, null);

        request.Model.Should().Be("gpt-4o");
        request.Stream.Should().BeFalse();
        request.Input.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void CreateRequest_FastMode_UsesFastModelId()
    {
        var service = CreateService();
        var history = new MessageList();
        var options = new ChatOptions
        {
            FastMode = true,
            FastModelId = "gpt-4o-mini"
        };

        var request = service.CreateRequest(history, options, stream: false, null);

        request.Model.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public void CreateRequest_FastModeWithoutFastModelId_KeepsConfigModel()
    {
        var service = CreateService();
        var options = new ChatOptions { FastMode = true };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Model.Should().Be("gpt-4o");
    }

    [Fact]
    public void CreateRequest_StreamEnabled_SetsStreamTrue()
    {
        var service = CreateService();

        var request = service.CreateRequest(new MessageList(), null, stream: true, null);

        request.Stream.Should().BeTrue();
    }

    [Fact]
    public void CreateRequest_SystemMessage_MapsToInstructions()
    {
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
    public void CreateRequest_MultipleSystemMessages_ConcatenatesInstructions()
    {
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
    public void CreateRequest_UserMessage_InputArrayContainsUserRole()
    {
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
    public void CreateRequest_AssistantMessage_UsesOutputTextContentType()
    {
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
    public void CreateRequest_TransfersTemperatureTopPMaxOutputTokens()
    {
        var service = CreateService();
        var options = new ChatOptions
        {
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
    public void CreateRequest_EffortLevel_SetsReasoningEffort()
    {
        var service = CreateService();
        var options = new ChatOptions { EffortLevel = EffortLevel.Medium };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Reasoning.Should().NotBeNull();
        request.Reasoning!.Effort.Should().Be("medium");
    }

    [Fact]
    public void CreateRequest_ThinkingEnabled_SetsReasoningWithHighEffort()
    {
        var service = CreateService();
        var options = new ChatOptions { ThinkingEnabled = true };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Reasoning.Should().NotBeNull();
        request.Reasoning!.Effort.Should().Be("high",
            "ThinkingEnabled 无 EffortLevel 时默认 high effort 开启思考模式");
    }

    [Fact]
    public void CreateRequest_ThinkingDisabled_DoesNotSetReasoning()
    {
        var service = CreateService();
        var options = new ChatOptions { ThinkingEnabled = false };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Reasoning.Should().BeNull();
    }

    [Fact]
    public void CreateRequest_ToolChoiceAutoWithKernel_BuildsTools()
    {
        var service = CreateService();
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
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
    public void CreateRequest_ToolChoiceAutoWithoutKernel_DoesNotAddTools()
    {
        var service = CreateService();
        var options = new ChatOptions { ToolChoice = ToolChoice.AutoInvoke };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Tools.Should().BeNull();
    }

    [Fact]
    public void CreateRequest_SpecialCharactersInContent_EscapedProperly()
    {
        var service = CreateService();
        var history = new MessageList { new(MessageRole.User, "hello \"world\"\nnewline") };

        var request = service.CreateRequest(history, null, stream: false, null);

        var text = request.Input[0].GetProperty("content")[0].GetProperty("text").GetString();
        text.Should().Be("hello \"world\"\nnewline");
    }

    #endregion

    #region ConvertToApiMessages

    [Fact]
    public void ConvertToApiMessages_TextResponse_ReturnsAssistantMessageWithText()
    {
        var response = new ResponsesResponse
        {
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
    public void ConvertToApiMessages_MultipleContentTexts_Concatenated()
    {
        var response = new ResponsesResponse
        {
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
    public void ConvertToApiMessages_FunctionCall_ReturnsToolCallsMetadata()
    {
        var response = new ResponsesResponse
        {
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
    public void ConvertToApiMessages_Usage_MapsToTokenUsage()
    {
        var response = new ResponsesResponse
        {
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
            Usage = new ResponsesUsage
            {
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
    public void ConvertToApiMessages_EmptyOutput_ReturnsEmptyContentMessage()
    {
        var response = new ResponsesResponse
        {
            Id = "resp-5",
            Output = []
        };

        var messages = ResponsesQueryService.ConvertToApiMessages(response);

        messages.Should().ContainSingle();
        messages[0].Content.Should().Be("");
    }

    [Fact]
    public void ConvertToApiMessages_TextAndFunctionCall_PrioritizesToolCalls()
    {
        var response = new ResponsesResponse
        {
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
}
