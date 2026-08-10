namespace Llm.Tests.Adapters.LLM.QueryServices.OpenAI;

using System.Text.Json;
using Api.LLM.QueryServices;
using Api.LLM.QueryServices.OpenAI;

public class OpenAIQueryServiceTests
{
    private static OpenAIQueryService CreateService(string provider = "openai")
    {
        var kind = ProtocolKind.OpenAiCompatible;
        var config = new ProviderConfig
        {
            Vendor = provider,
            ApiKey = "sk-test",
            ModelId = "gpt-4o",
            Definition = new FallbackProviderDefinition(kind)
        };
        return new OpenAIQueryService(config);
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
        request.Messages.Should().ContainSingle();
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
    public void CreateRequest_StreamEnabled_IncludesUsageOption()
    {
        var service = CreateService();

        var request = service.CreateRequest(new MessageList(), null, stream: true, null);

        request.StreamOptions.Should().NotBeNull();
        request.StreamOptions!.IncludeUsage.Should().BeTrue();
    }

    [Fact]
    public void CreateRequest_EffortLevel_SetsReasoningEffort()
    {
        var service = CreateService();
        var options = new ChatOptions { EffortLevel = EffortLevel.Medium };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.ReasoningEffort.Should().Be("medium");
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
        request.Tools![0].Function.Name.Should().Be("TestTool");
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
    public void CreateRequest_TransfersTemperatureMaxTokensTopP()
    {
        var service = CreateService();
        var options = new ChatOptions
        {
            Temperature = 0.5f,
            MaxTokens = 100,
            TopP = 0.9f,
            FrequencyPenalty = 0.1f,
            PresencePenalty = 0.2f
        };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Temperature.Should().Be(0.5f);
        request.MaxTokens.Should().Be(100);
        request.TopP.Should().Be(0.9f);
        request.FrequencyPenalty.Should().Be(0.1f);
        request.PresencePenalty.Should().Be(0.2f);
    }

    #endregion

    #region ConvertToOpenAIMessage

    [Fact]
    public void ConvertToOpenAIMessage_UserMessage_MapsRoleAndContent()
    {
        var message = new ApiMessage(MessageRole.User, "hello");

        var result = OpenAIQueryService.ConvertToOpenAIMessage(message);

        result.Role.Should().Be("user");
        result.Content.Should().Be("hello");
    }

    [Fact]
    public void ConvertToOpenAIMessage_AssistantMessage_MapsRoleAndContent()
    {
        var message = new ApiMessage(MessageRole.Assistant, "hi there");

        var result = OpenAIQueryService.ConvertToOpenAIMessage(message);

        result.Role.Should().Be("assistant");
        result.Content.Should().Be("hi there");
    }

    [Fact]
    public void ConvertToOpenAIMessage_AssistantWithToolCalls_MapsToolCallsAndClearsContent()
    {
        var entries = new[]
        {
            new ToolCallEntry { Id = "1", Name = "ToolA", Arguments = "{}" }
        };
        var metadata = ToolCallEntry.BuildAssistantMetadata(entries);
        var message = new ApiMessage(MessageRole.Assistant, "ignored", metadata);

        var result = OpenAIQueryService.ConvertToOpenAIMessage(message);

        result.ToolCalls.Should().NotBeNull();
        result.ToolCalls.Should().ContainSingle();
        result.ToolCalls![0].Id.Should().Be("1");
        result.ToolCalls[0].Function!.Name.Should().Be("ToolA");
        result.Content.Should().BeNull();
    }

    [Fact]
    public void ConvertToOpenAIMessage_ToolMessage_MapsToolCallId()
    {
        var metadata = ToolCallEntry.BuildToolResultMetadata("call-1", "ToolA");
        var message = new ApiMessage(MessageRole.Tool, "result", metadata);

        var result = OpenAIQueryService.ConvertToOpenAIMessage(message);

        result.Role.Should().Be("tool");
        result.ToolCallId.Should().Be("call-1");
        result.Name.Should().Be("ToolA");
        result.Content.Should().Be("result");
    }

    #endregion

    #region ConvertToApiMessage

    [Fact]
    public void ConvertToApiMessage_WithUsage_IncludesUsageMetadata()
    {
        var choice = new OpenAIChoice
        {
            Message = new OpenAIApiMessage { Role = "assistant", Content = "ok" },
            FinishReason = "stop"
        };
        var usage = new OpenAIUsage
        {
            PromptTokens = 10,
            CompletionTokens = 5,
            TotalTokens = 15
        };

        var result = OpenAIQueryService.ConvertToApiMessage(choice, usage);

        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey("Usage");
        result.Metadata.Should().ContainKey("FinishReason");
    }

    [Fact]
    public void ConvertToApiMessage_WithReasoningContent_IncludesMetadata()
    {
        var choice = new OpenAIChoice
        {
            Message = new OpenAIApiMessage
            {
                Role = "assistant",
                Content = "ok",
                ReasoningContent = "thinking"
            },
            FinishReason = "stop"
        };

        var result = OpenAIQueryService.ConvertToApiMessage(choice, null);

        result.Metadata.Should().ContainKey("reasoning_content");
        result.Metadata!["reasoning_content"].GetString().Should().Be("thinking");
    }

    [Fact]
    public void ConvertToApiMessage_WithToolCalls_IncludesToolCallMetadata()
    {
        var choice = new OpenAIChoice
        {
            Message = new OpenAIApiMessage
            {
                Role = "assistant",
                Content = "calling",
                ToolCalls =
                [
                    new OpenAIToolCall
                    {
                        Id = "call-1",
                        Function = new OpenAIToolCallFunction { Name = "ToolA", Arguments = "{\"x\":1}" }
                    }
                ]
            },
            FinishReason = "tool_calls"
        };

        var result = OpenAIQueryService.ConvertToApiMessage(choice, null);

        result.Metadata.Should().ContainKeys("ToolCall", "ToolCallId", "ToolCallArguments", "ToolCalls");
        result.Metadata!["ToolCall"].GetString().Should().Be("ToolA");
        result.Metadata["ToolCallId"].GetString().Should().Be("call-1");
    }

    [Fact]
    public void ConvertToApiMessage_InvalidRole_FallsBackToAssistant()
    {
        var choice = new OpenAIChoice
        {
            Message = new OpenAIApiMessage { Role = "unknown", Content = "ok" },
            FinishReason = "stop"
        };

        var result = OpenAIQueryService.ConvertToApiMessage(choice, null);

        result.Role.Should().Be(MessageRole.Assistant);
    }

    #endregion
}
