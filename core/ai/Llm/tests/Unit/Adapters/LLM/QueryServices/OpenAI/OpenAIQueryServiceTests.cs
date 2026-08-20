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
    public void CreateRequest_ThinkingEnabled_SetsThinkingField()
    {
        var service = CreateService();
        var options = new ChatOptions { ThinkingEnabled = true };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Thinking.Should().NotBeNull();
        request.Thinking!.Type.Should().Be("enabled",
            "DeepSeek V4 通过 thinking type enabled 开启思考模式");
    }

    [Fact]
    public void CreateRequest_ThinkingDisabled_DoesNotSetThinking()
    {
        var service = CreateService();
        var options = new ChatOptions { ThinkingEnabled = false };

        var request = service.CreateRequest(new MessageList(), options, stream: false, null);

        request.Thinking.Should().BeNull("ThinkingEnabled=false 时不发 thinking 字段");
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

        result.Metadata.Should().ContainKeys("AllToolCalls", "ToolCalls");
        result.Metadata!["AllToolCalls"].ValueKind.Should().Be(JsonValueKind.Array);
        result.Metadata!["AllToolCalls"].GetArrayLength().Should().Be(1);
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

    #region Two-Phase Tool Loading — BuildToolsFromKernel

    [Fact]
    public void BuildToolsFromKernel_OnlyCoreTools_ToolsPopulated_ToolGroupsEmpty()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.CoreTools, [
            new ToolDef("read", "Read a file"),
            new ToolDef("write", "Write a file")
        ]));

        var (tools, toolGroups) = OpenAIQueryService.BuildToolsFromKernel(kernel);

        tools.Should().HaveCount(2);
        tools.Select(t => t.Function.Name).Should().Contain(["read", "write"]);
        toolGroups.Should().BeEmpty();
    }

    [Fact]
    public void BuildToolsFromKernel_OnlyMcpTools_ToolsEmpty_ToolGroupsPopulated()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.server1.tool1", "MCP tool 1"),
            new ToolDef("mcp.server2.tool2", "MCP tool 2")
        ]));

        var (tools, toolGroups) = OpenAIQueryService.BuildToolsFromKernel(kernel);

        tools.Should().BeEmpty();
        toolGroups.Should().ContainSingle();
        toolGroups[0].Name.Should().Be(ToolGroupNameConstants.McpTools);
        toolGroups[0].Tools.Should().Contain(["mcp.server1.tool1", "mcp.server2.tool2"]);
    }

    [Fact]
    public void BuildToolsFromKernel_MixedTools_CoreToolsInTools_McpToolsInGroups()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.CoreTools, [
            new ToolDef("read", "Read a file")
        ]));
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1")
        ]));

        var (tools, toolGroups) = OpenAIQueryService.BuildToolsFromKernel(kernel);

        tools.Should().ContainSingle();
        tools[0].Function.Name.Should().Be("read");
        toolGroups.Should().ContainSingle();
        toolGroups[0].Name.Should().Be(ToolGroupNameConstants.McpTools);
        toolGroups[0].Tools.Should().ContainSingle().Which.Should().Be("mcp.tool1");
    }

    #endregion

    #region Two-Phase Tool Loading — CreateSecondRequestWithDescriptions

    [Fact]
    public void CreateSecondRequestWithDescriptions_ValidToolNames_BuildsDescriptions()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1"),
            new ToolDef("mcp.tool2", "MCP tool 2")
        ]));

        var originalRequest = new OpenAIChatRequest
        {
            Model = "gpt-4o",
            Messages = [new OpenAIApiMessage { Role = "user", Content = "hi" }],
            Stream = true
        };
        var descRequestContent = """{"tools":["mcp.tool1","mcp.tool2"]}""";

        var secondRequest = OpenAIQueryService.CreateSecondRequestWithDescriptions(originalRequest, descRequestContent, kernel);

        secondRequest.ToolDescriptions.Should().HaveCount(2);
        secondRequest.ToolDescriptions!.Select(t => t.Function.Name).Should().Contain(["mcp.tool1", "mcp.tool2"]);
        secondRequest.Model.Should().Be("gpt-4o");
        secondRequest.Stream.Should().BeTrue();
    }

    [Fact]
    public void CreateSecondRequestWithDescriptions_UnknownToolNames_DescriptionsEmpty()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1")
        ]));

        var originalRequest = new OpenAIChatRequest { Model = "gpt-4o" };
        var descRequestContent = """{"tools":["nonexistent.tool"]}""";

        var secondRequest = OpenAIQueryService.CreateSecondRequestWithDescriptions(originalRequest, descRequestContent, kernel);

        secondRequest.ToolDescriptions.Should().BeEmpty();
    }

    [Fact]
    public void CreateSecondRequestWithDescriptions_PreservesOriginalFields()
    {
        var kernel = new ChatClient(new Mock<IQueryService>().Object);
        kernel.Plugins.Add(new ToolGroup(ToolGroupNameConstants.McpTools, [
            new ToolDef("mcp.tool1", "MCP tool 1")
        ]));

        var originalRequest = new OpenAIChatRequest
        {
            Model = "gpt-4o",
            Messages = [new OpenAIApiMessage { Role = "user", Content = "test" }],
            Stream = true,
            Temperature = 0.7f,
            MaxTokens = 1000,
            Tools = [new OpenAITool { Function = new OpenAIFunctionDefinition { Name = "read" } }],
            ToolGroups = [new OpenAIToolGroup { Name = "mcp_tools", Tools = ["mcp.tool1"] }]
        };
        var descRequestContent = """{"tools":["mcp.tool1"]}""";

        var secondRequest = OpenAIQueryService.CreateSecondRequestWithDescriptions(originalRequest, descRequestContent, kernel);

        secondRequest.Model.Should().Be("gpt-4o");
        secondRequest.Temperature.Should().Be(0.7f);
        secondRequest.MaxTokens.Should().Be(1000);
        secondRequest.Tools.Should().BeSameAs(originalRequest.Tools);
        secondRequest.ToolGroups.Should().BeSameAs(originalRequest.ToolGroups);
        secondRequest.ToolDescriptions.Should().ContainSingle();
    }

    #endregion
}
