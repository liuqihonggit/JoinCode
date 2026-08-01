namespace Llm.Tests.Adapters.LLM.Serialization;

public class AnthropicJsonContextTests
{
    [Fact]
    public void AnthropicMessagesRequest_RoundTrip_SerializesModelAndMessages()
    {
        var request = new AnthropicMessagesRequest
        {
            Model = "claude",
            MaxTokens = 100,
            Stream = true,
            Messages =
            [
                new AnthropicMessage { Role = "user", Content = "hi" }
            ]
        };

        var json = JsonSerializer.Serialize(request, AnthropicJsonContext.Default.AnthropicMessagesRequest);
        var deserialized = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicMessagesRequest);

        deserialized.Should().NotBeNull();
        deserialized!.Model.Should().Be("claude");
        deserialized.MaxTokens.Should().Be(100);
        deserialized.Stream.Should().BeTrue();
    }

    [Fact]
    public void AnthropicMessagesResponse_RoundTrip_PreservesContentBlocks()
    {
        var response = new AnthropicMessagesResponse
        {
            Id = "msg-1",
            Model = "claude",
            StopReason = AnthropicStopReason.EndTurn,
            Content =
            [
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.Text,
                    Text = "Hello"
                }
            ]
        };

        var json = JsonSerializer.Serialize(response, AnthropicJsonContext.Default.AnthropicMessagesResponse);
        var deserialized = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicMessagesResponse);

        deserialized.Should().NotBeNull();
        deserialized!.StopReason.Should().Be(AnthropicStopReason.EndTurn);
        deserialized.Content[0].Type.Should().Be(AnthropicContentBlockType.Text);
    }

    [Fact]
    public void AnthropicStreamingEvent_RoundTrip_PreservesType()
    {
        var evt = new AnthropicStreamingEvent
        {
            Type = AnthropicStreamingEventType.ContentBlockDelta,
            Index = 0,
            Delta = new AnthropicStreamingDelta
            {
                Type = AnthropicDeltaType.TextDelta,
                Text = "delta"
            }
        };

        var json = JsonSerializer.Serialize(evt, AnthropicJsonContext.Default.AnthropicStreamingEvent);
        var deserialized = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicStreamingEvent);

        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be(AnthropicStreamingEventType.ContentBlockDelta);
        deserialized.Delta!.Type.Should().Be(AnthropicDeltaType.TextDelta);
    }

    [Fact]
    public void AnthropicToolDefinition_RoundTrip_PreservesNameAndSchema()
    {
        var tool = new AnthropicToolDefinition
        {
            Name = "ToolA",
            Description = "desc",
            InputSchema = new AnthropicInputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, AnthropicSchemaProperty>
                {
                    ["x"] = new() { Type = "integer" }
                }
            }
        };

        var json = JsonSerializer.Serialize(tool, AnthropicJsonContext.Default.AnthropicToolDefinition);
        var deserialized = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicToolDefinition);

        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("ToolA");
        deserialized.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void AnthropicContextManagement_RoundTrip_PreservesStrategies()
    {
        var management = new AnthropicContextManagement
        {
            Edits =
            [
                new AnthropicClearThinkingStrategy { Keep = 10 }
            ]
        };

        var json = JsonSerializer.Serialize(management, AnthropicJsonContext.Default.AnthropicContextManagement);
        var deserialized = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicContextManagement);

        deserialized.Should().NotBeNull();
        deserialized!.Edits.Should().ContainSingle();
    }

    [Fact]
    public void AnthropicToolReferenceBlock_RoundTrip_PreservesToolName()
    {
        var block = new AnthropicToolReferenceBlock { ToolName = "ToolA" };

        var json = JsonSerializer.Serialize(block, AnthropicJsonContext.Default.AnthropicToolReferenceBlock);
        var deserialized = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicToolReferenceBlock);

        deserialized!.ToolName.Should().Be("ToolA");
    }
}
