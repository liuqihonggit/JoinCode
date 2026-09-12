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

    [Fact]
    public void OpenAIChatRequest_WithThinking_SerializesThinkingField()
    {
        var request = new OpenAIChatRequest
        {
            Model = "deepseek-v4-pro",
            Messages = [new OpenAIApiMessage { Role = "user", Content = "hi" }],
            Thinking = new OpenAIThinkingOptions { Type = "enabled" }
        };

        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.OpenAIChatRequest);

        json.Should().Contain("\"thinking\"");
        json.Should().Contain("\"type\":\"enabled\"");

        var deserialized = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIChatRequest);
        deserialized!.Thinking.Should().NotBeNull();
        deserialized.Thinking!.Type.Should().Be("enabled");
    }

    [Fact]
    public void OpenAIChatRequest_WithoutThinking_DoesNotSerializeThinkingField()
    {
        var request = new OpenAIChatRequest
        {
            Model = "deepseek-v4-pro",
            Messages = [new OpenAIApiMessage { Role = "user", Content = "hi" }]
        };

        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.OpenAIChatRequest);

        json.Should().NotContain("\"thinking\"",
            "Thinking 为 null 时不应序列化,JsonIgnore WhenWritingNull");
    }

    [Fact]
    public void OpenAIApiMessage_TextContent_SerializesContentAsString()
    {
        var msg = new OpenAIApiMessage { Role = "user", Content = "hello" };

        var json = JsonSerializer.Serialize(msg, NativeJsonContext.Default.OpenAIApiMessage);

        json.Should().Contain("\"content\":\"hello\"");
        json.Should().NotContain("\"type\":\"text\"", "纯文本时 content 应为 string 而非数组");
    }

    [Fact]
    public void OpenAIApiMessage_MultimodalContent_SerializesAsArrayWithImageUrl()
    {
        var msg = new OpenAIApiMessage
        {
            Role = "user",
            Content = new List<OpenAIContentPart>
            {
                new() { Type = "text", Text = "What is this?" },
                new() { Type = "image_url", ImageUrl = new OpenAIImageUrl { Url = "data:image/png;base64,iVBOR" } }
            }
        };

        var json = JsonSerializer.Serialize(msg, NativeJsonContext.Default.OpenAIApiMessage);

        json.Should().Contain("\"type\":\"image_url\"");
        json.Should().Contain("\"image_url\":{\"url\":\"data:image/png;base64,iVBOR\"}");
        json.Should().Contain("\"type\":\"text\",\"text\":\"What is this?\"");
    }

    [Fact]
    public void OpenAIApiMessage_MultimodalContent_RoundTripsImageUrlPart()
    {
        var msg = new OpenAIApiMessage
        {
            Role = "user",
            Content = new List<OpenAIContentPart>
            {
                new() { Type = "image_url", ImageUrl = new OpenAIImageUrl { Url = "data:image/jpeg;base64,abc" } }
            }
        };

        var json = JsonSerializer.Serialize(msg, NativeJsonContext.Default.OpenAIApiMessage);
        var deserialized = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIApiMessage);

        deserialized!.Content!.Parts.Should().NotBeNull();
        deserialized.Content.Parts!.Should().ContainSingle();
        deserialized.Content.Parts[0].Type.Should().Be("image_url");
        deserialized.Content.Parts[0].ImageUrl!.Url.Should().Be("data:image/jpeg;base64,abc");
    }
}
