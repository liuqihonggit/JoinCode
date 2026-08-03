namespace Llm.Tests.Adapters.LLM.Serialization;

public class NativeJsonContextTests
{
    [Fact]
    public void OpenAIChatRequest_RoundTrip_SerializesModelAndMessages()
    {
        var request = new OpenAIChatRequest
        {
            Model = "gpt-4o",
            Stream = true,
            Temperature = 0.7f,
            Messages =
            [
                new OpenAIApiMessage { Role = "user", Content = "hi" }
            ]
        };

        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.OpenAIChatRequest);
        var deserialized = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIChatRequest);

        deserialized.Should().NotBeNull();
        deserialized!.Model.Should().Be("gpt-4o");
        deserialized.Stream.Should().BeTrue();
        deserialized.Temperature.Should().Be(0.7f);
        deserialized.Messages.Should().ContainSingle();
    }

    [Fact]
    public void OpenAIChatResponse_RoundTrip_PreservesChoices()
    {
        var response = new OpenAIChatResponse
        {
            Id = "resp-1",
            Model = "gpt-4o",
            Choices =
            [
                new OpenAIChoice
                {
                    Index = 0,
                    Message = new OpenAIApiMessage { Role = "assistant", Content = "hello" },
                    FinishReason = "stop"
                }
            ]
        };

        var json = JsonSerializer.Serialize(response, NativeJsonContext.Default.OpenAIChatResponse);
        var deserialized = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIChatResponse);

        deserialized.Should().NotBeNull();
        deserialized!.Choices.Should().ContainSingle();
        deserialized.Choices[0].FinishReason.Should().Be("stop");
    }

    [Fact]
    public void OpenAIChatChunk_RoundTrip_PreservesUsage()
    {
        var chunk = new OpenAIChatChunk
        {
            Id = "chunk-1",
            Model = "gpt-4o",
            Usage = new OpenAIUsage
            {
                PromptTokens = 10,
                CompletionTokens = 5,
                TotalTokens = 15
            }
        };

        var json = JsonSerializer.Serialize(chunk, NativeJsonContext.Default.OpenAIChatChunk);
        var deserialized = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIChatChunk);

        deserialized!.Usage.Should().NotBeNull();
        deserialized.Usage!.TotalTokens.Should().Be(15);
    }

    [Fact]
    public void OpenAITool_RoundTrip_PreservesFunction()
    {
        var tool = new OpenAITool
        {
            Function = new OpenAIFunctionDefinition
            {
                Name = "ToolA",
                Description = "desc",
                Parameters = new OpenAIFunctionParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenAIParameterProperty>
                    {
                        ["x"] = new() { Type = "integer", Description = "param" }
                    },
                    Required = new List<string> { "x" }
                }
            }
        };

        var json = JsonSerializer.Serialize(tool, NativeJsonContext.Default.OpenAITool);
        var deserialized = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAITool);

        deserialized!.Function.Name.Should().Be("ToolA");
        deserialized.Function.Parameters!.Required.Should().Contain("x");
    }

    [Fact]
    public void TokenUsage_RoundTrip_PreservesTotals()
    {
        var usage = new TokenUsage(10, 5)
        {
            CacheCreationInputTokens = 1,
            CacheReadInputTokens = 2,
            ReasoningTokens = 3
        };

        var json = JsonSerializer.Serialize(usage, NativeJsonContext.Default.TokenUsage);
        var deserialized = JsonSerializer.Deserialize(json, NativeJsonContext.Default.TokenUsage);

        deserialized.Should().NotBeNull();
        deserialized!.PromptTokens.Should().Be(10);
        deserialized.CompletionTokens.Should().Be(5);
        deserialized.TotalTokens.Should().Be(15);
        deserialized.CacheCreationInputTokens.Should().Be(1);
    }
}
