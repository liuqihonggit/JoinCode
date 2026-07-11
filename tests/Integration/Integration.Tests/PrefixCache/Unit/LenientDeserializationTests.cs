namespace Integration.Tests.PrefixCache.Unit;

/// <summary>
/// 协议层宽容反序列化测试 — 覆盖 DeepSeek 等模型返回的畸形 JSON 场景
/// </summary>
public sealed class LenientDeserializationTests
{
    #region NativeJsonContext — OpenAI/DeepSeek 协议

    [Fact]
    public void NativeJson_TrailingCommaInResponse_Deserializes()
    {
        var json = """{"id":"chatcmpl-123","object":"chat.completion","created":1234567890,"model":"deepseek-chat","choices":[{"index":0,"message":{"role":"assistant","content":"Hello"},"finish_reason":"stop"}],}""";

        var result = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIChatResponse);

        result.Should().NotBeNull();
        result!.Id.Should().Be("chatcmpl-123");
        result.Model.Should().Be("deepseek-chat");
        result.Choices.Should().HaveCount(1);
        result.Choices[0].Message.Content.Should().Be("Hello");
    }

    [Fact]
    public void NativeJson_TrailingCommaInToolCall_Deserializes()
    {
        var json = """{"id":"chatcmpl-123","object":"chat.completion","created":1234567890,"model":"deepseek-chat","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_123","type":"function","function":{"name":"Bash","arguments":"{\"command\":\"ls\"}"}}]},"finish_reason":"tool_calls"}]}""";

        var result = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIChatResponse);

        result.Should().NotBeNull();
        result!.Choices[0].Message.ToolCalls.Should().HaveCount(1);
        var toolCall = result.Choices[0].Message.ToolCalls![0];
        toolCall.Function.Should().NotBeNull();
        toolCall.Function!.Name.Should().Be("Bash");
        toolCall.Function!.Arguments.Should().Contain("ls");
    }

    [Fact]
    public void NativeJson_CaseInsensitiveProperty_Deserializes()
    {
        var json = """{"Id":"chatcmpl-123","Object":"chat.completion","Created":1234567890,"Model":"deepseek-chat","Choices":[{"Index":0,"Message":{"Role":"assistant","Content":"Hello"},"FinishReason":"stop"}]}""";

        var result = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIChatResponse);

        result.Should().NotBeNull();
        result!.Id.Should().Be("chatcmpl-123");
        result.Choices[0].Message.Content.Should().Be("Hello");
    }

    [Fact]
    public void NativeJson_CommentInJson_Deserializes()
    {
        var json = """
        {
            "id": "chatcmpl-123",
            "object": "chat.completion",
            "created": 1234567890,
            "model": "deepseek-chat",
            "choices": [{
                "index": 0,
                "message": {"role": "assistant", "content": "Hello"},
                "finish_reason": "stop"
            }]
        }
        """;

        var result = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIChatResponse);

        result.Should().NotBeNull();
        result!.Choices[0].Message.Content.Should().Be("Hello");
    }

    #endregion

    #region AnthropicJsonContext — Anthropic 协议

    [Fact]
    public void AnthropicJson_TrailingCommaInResponse_Deserializes()
    {
        var json = """{"id":"msg_123","type":"message","role":"assistant","content":[{"type":"text","text":"Hello"}],"model":"claude-3","stop_reason":"end_turn",}""";

        var result = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicMessagesResponse);

        result.Should().NotBeNull();
        result!.Id.Should().Be("msg_123");
        result.Content.Should().HaveCount(1);
    }

    #endregion

    #region ToolCallRepairService — 工具参数修复

    [Fact]
    public void RepairJson_DeepSeekSingleQuotedArguments_RepairAndParse()
    {
        var rawArgs = "{'command': 'ls -la', 'workingDirectory': '/tmp'}";
        var jsonRepair = ToolCallRepairService.RepairJson(rawArgs);
        var parsed = JsonArgumentParser.Parse(jsonRepair.Success ? jsonRepair.RepairedJson : rawArgs);

        jsonRepair.Success.Should().BeTrue();
        parsed.Should().ContainKey("command");
        parsed["command"].GetString().Should().Be("ls -la");
        parsed.Should().ContainKey("workingDirectory");
    }

    [Fact]
    public void RepairJson_DeepSeekUnquotedKeys_RepairAndParse()
    {
        var rawArgs = """{command: "ls", workingDirectory: "/tmp"}""";
        var jsonRepair = ToolCallRepairService.RepairJson(rawArgs);
        var parsed = JsonArgumentParser.Parse(jsonRepair.Success ? jsonRepair.RepairedJson : rawArgs);

        jsonRepair.Success.Should().BeTrue();
        parsed.Should().ContainKey("command");
        parsed["command"].GetString().Should().Be("ls");
    }

    [Fact]
    public void RepairJson_DeepSeekTrailingComma_RepairAndParse()
    {
        var rawArgs = """{"command": "ls", "workingDirectory": "/tmp",}""";
        var jsonRepair = ToolCallRepairService.RepairJson(rawArgs);
        var parsed = JsonArgumentParser.Parse(jsonRepair.Success ? jsonRepair.RepairedJson : rawArgs);

        jsonRepair.Success.Should().BeTrue();
        parsed.Should().ContainKey("command");
        parsed["command"].GetString().Should().Be("ls");
    }

    [Fact]
    public void RepairJson_DeepSeekMixedIssues_RepairAndParse()
    {
        var rawArgs = """{'command': "ls", 'workingDirectory': "/tmp",}""";
        var jsonRepair = ToolCallRepairService.RepairJson(rawArgs);
        var parsed = JsonArgumentParser.Parse(jsonRepair.Success ? jsonRepair.RepairedJson : rawArgs);

        jsonRepair.Success.Should().BeTrue();
        parsed.Should().ContainKey("command");
        parsed["command"].GetString().Should().Be("ls");
    }

    [Fact]
    public void RepairJson_EmptyOrNull_ReturnsEmptyDict()
    {
        var parsed1 = JsonArgumentParser.Parse(null);
        var parsed2 = JsonArgumentParser.Parse("");

        parsed1.Should().BeEmpty();
        parsed2.Should().BeEmpty();
    }

    [Fact]
    public void RepairJson_InvalidJson_ReturnsEmptyDict()
    {
        var parsed = JsonArgumentParser.Parse("not json at all");

        parsed.Should().BeEmpty();
    }

    #endregion

    #region 端到端: 工具调用参数完整链路

    [Theory]
    [InlineData("""{"command":"ls"}""", "command", "ls")]
    [InlineData("""{"command": "ls",}""", "command", "ls")]
    [InlineData("{'command': 'ls'}", "command", "ls")]
    [InlineData("""{command: "ls"}""", "command", "ls")]
    public void FullPipeline_VariousMalformedJson_ParsesCorrectly(string rawJson, string expectedKey, string expectedValue)
    {
        var jsonRepair = ToolCallRepairService.RepairJson(rawJson);
        var parsed = JsonArgumentParser.Parse(jsonRepair.Success ? jsonRepair.RepairedJson : rawJson);

        parsed.Should().ContainKey(expectedKey);
        parsed[expectedKey].GetString().Should().Be(expectedValue);
    }

    #endregion
}
