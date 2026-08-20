namespace Llm.Tests.Adapters.LLM.Serialization;

/// <summary>
/// Responses API DTO 序列化测试 — 验证 DeepSeek/OpenAI Responses API 格式
/// 端点 /responses,请求用 input+instructions,响应用 output 数组,流式用 event SSE
/// </summary>
public class ResponsesTypesTests
{
    [Fact]
    public void ResponsesRequest_WithStringInput_SerializesCorrectly()
    {
        var request = new ResponsesRequest
        {
            Model = "deepseek-v4-flash",
            Input = JsonDocument.Parse("\"Hi, how are you?\"").RootElement,
            Instructions = "You are a helpful assistant.",
            Stream = false
        };

        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.ResponsesRequest);

        json.Should().Contain("\"model\":\"deepseek-v4-flash\"");
        json.Should().Contain("\"input\":\"Hi, how are you?\"");
        json.Should().Contain("\"instructions\":\"You are a helpful assistant.\"");
        json.Should().Contain("\"stream\":false");
    }

    [Fact]
    public void ResponsesRequest_WithReasoning_SerializesReasoningEffort()
    {
        var request = new ResponsesRequest
        {
            Model = "deepseek-v4-pro",
            Input = JsonDocument.Parse("\"think about this\"").RootElement,
            Reasoning = new ResponsesReasoning { Effort = "high" }
        };

        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.ResponsesRequest);

        json.Should().Contain("\"reasoning\"");
        json.Should().Contain("\"effort\":\"high\"");
    }

    [Fact]
    public void ResponsesRequest_WithoutOptionalFields_DoesNotSerializeThem()
    {
        var request = new ResponsesRequest
        {
            Model = "deepseek-v4-flash",
            Input = JsonDocument.Parse("\"hi\"").RootElement
        };

        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.ResponsesRequest);

        json.Should().NotContain("\"instructions\"");
        json.Should().NotContain("\"reasoning\"");
        json.Should().NotContain("\"tools\"");
        json.Should().NotContain("\"temperature\"");
        json.Should().NotContain("\"max_output_tokens\"");
    }

    [Fact]
    public void ResponsesRequest_WithTools_SerializesTools()
    {
        var request = new ResponsesRequest
        {
            Model = "deepseek-v4-flash",
            Input = JsonDocument.Parse("\"use tool\"").RootElement,
            Tools = [new ResponsesTool { Type = "function", Name = "get_weather", Description = "Get weather", Parameters = JsonDocument.Parse("{\"type\":\"object\"}").RootElement }]
        };

        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.ResponsesRequest);

        json.Should().Contain("\"tools\"");
        json.Should().Contain("\"name\":\"get_weather\"");
    }

    [Fact]
    public void ResponsesResponse_RoundTrip_PreservesOutputAndUsage()
    {
        var response = new ResponsesResponse
        {
            Id = "resp-123",
            Object = "response",
            Model = "deepseek-v4-flash",
            Status = "completed",
            OutputText = "Hello!",
            Output = [new ResponsesOutputItem
            {
                Type = "message",
                Role = "assistant",
                Content = [new ResponsesContent { Type = "output_text", Text = "Hello!" }]
            }],
            Usage = new ResponsesUsage
            {
                InputTokens = 10,
                OutputTokens = 5,
                InputTokensDetails = new ResponsesTokenDetails { CachedTokens = 3 },
                OutputTokensDetails = new ResponsesTokenDetails { ReasoningTokens = 0 }
            }
        };

        var json = JsonSerializer.Serialize(response, NativeJsonContext.Default.ResponsesResponse);
        var deserialized = JsonSerializer.Deserialize(json, NativeJsonContext.Default.ResponsesResponse);

        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be("resp-123");
        deserialized.Status.Should().Be("completed");
        deserialized.OutputText.Should().Be("Hello!");
        deserialized.Output.Should().ContainSingle();
        deserialized.Usage!.InputTokens.Should().Be(10);
        deserialized.Usage.OutputTokens.Should().Be(5);
        deserialized.Usage.InputTokensDetails!.CachedTokens.Should().Be(3);
    }

    [Fact]
    public void ResponsesResponse_WithFunctionCallOutput_PreservesFunctionCall()
    {
        var response = new ResponsesResponse
        {
            Id = "resp-456",
            Object = "response",
            Model = "deepseek-v4-flash",
            Status = "completed",
            Output = [new ResponsesOutputItem
            {
                Type = "function_call",
                Name = "get_weather",
                Arguments = "{\"location\":\"SF\"}",
                CallId = "call_123"
            }]
        };

        var json = JsonSerializer.Serialize(response, NativeJsonContext.Default.ResponsesResponse);
        var deserialized = JsonSerializer.Deserialize(json, NativeJsonContext.Default.ResponsesResponse);

        deserialized!.Output.Should().ContainSingle();
        deserialized.Output[0].Type.Should().Be("function_call");
        deserialized.Output[0].Name.Should().Be("get_weather");
        deserialized.Output[0].Arguments.Should().Be("{\"location\":\"SF\"}");
        deserialized.Output[0].CallId.Should().Be("call_123");
    }

    [Fact]
    public void ResponsesUsage_RoundTrip_PreservesAllTokenCounts()
    {
        var usage = new ResponsesUsage
        {
            InputTokens = 100,
            OutputTokens = 50,
            InputTokensDetails = new ResponsesTokenDetails { CachedTokens = 40 },
            OutputTokensDetails = new ResponsesTokenDetails { ReasoningTokens = 20 }
        };

        var json = JsonSerializer.Serialize(usage, NativeJsonContext.Default.ResponsesUsage);
        var deserialized = JsonSerializer.Deserialize(json, NativeJsonContext.Default.ResponsesUsage);

        deserialized!.InputTokens.Should().Be(100);
        deserialized.OutputTokens.Should().Be(50);
        deserialized.InputTokensDetails!.CachedTokens.Should().Be(40);
        deserialized.OutputTokensDetails!.ReasoningTokens.Should().Be(20);
    }
}
