namespace Llm.Tests.Adapters.Chat;


public class PipeQueryServiceTests {
    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException() {
        var act = () => new PipeQueryService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithConfig_DoesNotThrow() {
        var config = new PipeTransportConfig { PipeName = "test-pipe" };

        var act = () => new PipeQueryService(config);

        act.Should().NotThrow();
    }

    #region ConvertToMessage — tool_calls 元数据转换

    [Fact]
    public void ConvertToMessage_ToolRoleWithMetadata_SetsToolCallIdAndName() {
        var msg = new ApiMessage(MessageRole.Tool, "result",
            new Dictionary<string, JsonElement> {
                [MessageMetadataKeyEnumConstants.ToolCallId] = JsonElementHelper.FromString("call-1"),
                [MessageMetadataKeyEnumConstants.ToolName] = JsonElementHelper.FromString("grep")
            });

        var result = PipeQueryService.ConvertToMessage(msg);

        result.Role.Should().Be("tool");
        result.ToolCallId.Should().Be("call-1");
        result.Name.Should().Be("grep");
        result.Content!.Text.Should().Be("result");
    }

    [Fact]
    public void ConvertToMessage_AssistantWithToolCallsMetadata_SetsToolCalls() {
        var toolCallsJson = "[{\"Id\":\"call-1\",\"Name\":\"grep\",\"Arguments\":\"{\\\"q\\\":\\\"x\\\"}\"}]";
        var msg = new ApiMessage(MessageRole.Assistant, null,
            new Dictionary<string, JsonElement> {
                [MessageMetadataKeyEnumConstants.ToolCalls] = JsonElementHelper.FromJson(toolCallsJson)
            });

        var result = PipeQueryService.ConvertToMessage(msg);

        result.Role.Should().Be("assistant");
        result.ToolCalls.Should().NotBeEmpty();
        result.ToolCalls![0].Id.Should().Be("call-1");
        result.ToolCalls[0].Function!.Name.Should().Be("grep");
        result.ToolCalls[0].Function!.Arguments.Should().Be("{\"q\":\"x\"}");
    }

    [Fact]
    public void ConvertToMessage_AssistantWithMultipleToolCalls_AllMapped() {
        var toolCallsJson = "[{\"Id\":\"c1\",\"Name\":\"ls\",\"Arguments\":\"{}\"},{\"Id\":\"c2\",\"Name\":\"cat\",\"Arguments\":\"{}\"}]";
        var msg = new ApiMessage(MessageRole.Assistant, null,
            new Dictionary<string, JsonElement> {
                [MessageMetadataKeyEnumConstants.ToolCalls] = JsonElementHelper.FromJson(toolCallsJson)
            });

        var result = PipeQueryService.ConvertToMessage(msg);

        result.ToolCalls.Should().HaveCount(2);
        result.ToolCalls![0].Function!.Name.Should().Be("ls");
        result.ToolCalls[1].Function!.Name.Should().Be("cat");
    }

    [Fact]
    public void ConvertToMessage_ToolCallsItemMissingArguments_DefaultsToEmptyObject() {
        var toolCallsJson = "[{\"Id\":\"c1\",\"Name\":\"ls\"}]";
        var msg = new ApiMessage(MessageRole.Assistant, null,
            new Dictionary<string, JsonElement> {
                [MessageMetadataKeyEnumConstants.ToolCalls] = JsonElementHelper.FromJson(toolCallsJson)
            });

        var result = PipeQueryService.ConvertToMessage(msg);

        result.ToolCalls![0].Function!.Arguments.Should().Be("{}");
    }

    [Fact]
    public void ConvertToMessage_PlainUserMessage_NoToolCallFields() {
        var msg = new ApiMessage(MessageRole.User, "hi");

        var result = PipeQueryService.ConvertToMessage(msg);

        result.Role.Should().Be("user");
        result.ToolCalls.Should().BeEmpty();
        result.ToolCallId.Should().BeNull();
        result.Name.Should().BeNull();
    }

    [Fact]
    public void ConvertToMessage_ToolRoleWithoutMetadata_NoToolCallIdOrName() {
        var msg = new ApiMessage(MessageRole.Tool, "result");

        var result = PipeQueryService.ConvertToMessage(msg);

        result.Role.Should().Be("tool");
        result.ToolCallId.Should().BeNull();
        result.Name.Should().BeNull();
    }

    #endregion

    #region ConvertToApiMessage — tool_calls 响应处理

    [Fact]
    public void ConvertToApiMessage_WithToolCalls_PopulatesAllToolCallsMetadata() {
        var choice = new OpenAIChoice {
            Message = new OpenAIApiMessage {
                Role = "assistant",
                Content = "thinking",
                ToolCalls = [
                    new OpenAIToolCall {
                        Id = "call-1",
                        Function = new OpenAIToolCallFunction { Name = "grep", Arguments = "{\"q\":\"x\"}" }
                    }
                ]
            },
            FinishReason = "tool_calls"
        };

        var result = PipeQueryService.ConvertToApiMessage(choice);

        result.Role.Should().Be(MessageRole.Assistant);
        result.Content.Should().Be("thinking");
        result.Metadata.Should().ContainKey("AllToolCalls");
        result.Metadata!["FinishReason"].GetString().Should().Be("tool_calls");
        result.Metadata!["AllToolCalls"].GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void ConvertToApiMessage_WithoutToolCalls_OnlyFinishReasonMetadata() {
        var choice = new OpenAIChoice {
            Message = new OpenAIApiMessage { Role = "assistant", Content = "hello" },
            FinishReason = "stop"
        };

        var result = PipeQueryService.ConvertToApiMessage(choice);

        result.Metadata.Should().ContainKey("FinishReason");
        result.Metadata!["FinishReason"].GetString().Should().Be("stop");
        result.Metadata.Should().NotContainKey("AllToolCalls");
    }

    [Fact]
    public void ConvertToApiMessage_MultipleToolCalls_AllEntriesMapped() {
        var choice = new OpenAIChoice {
            Message = new OpenAIApiMessage {
                Role = "assistant",
                ToolCalls = [
                    new OpenAIToolCall { Id = "c1", Function = new OpenAIToolCallFunction { Name = "ls" } },
                    new OpenAIToolCall { Id = "c2", Function = new OpenAIToolCallFunction { Name = "cat" } }
                ]
            },
            FinishReason = "tool_calls"
        };

        var result = PipeQueryService.ConvertToApiMessage(choice);

        result.Metadata!["AllToolCalls"].GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void ConvertToApiMessage_NullFunctionDefaultsArgumentsToEmptyObject() {
        var choice = new OpenAIChoice {
            Message = new OpenAIApiMessage {
                Role = "assistant",
                ToolCalls = [new OpenAIToolCall { Id = "c1" }]
            },
            FinishReason = "tool_calls"
        };

        var result = PipeQueryService.ConvertToApiMessage(choice);

        result.Metadata.Should().ContainKey("AllToolCalls");
    }

    #endregion
}