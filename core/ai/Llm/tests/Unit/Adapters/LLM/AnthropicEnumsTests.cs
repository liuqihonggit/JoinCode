namespace Llm.Tests.Adapters.LLM;

using System.Text;
using System.Text.Json;

public class AnthropicEnumsTests
{
    #region AnthropicContentBlockTypeConverter

    [Theory]
    [InlineData("\"text\"", AnthropicContentBlockType.Text)]
    [InlineData("\"thinking\"", AnthropicContentBlockType.Thinking)]
    [InlineData("\"tool_use\"", AnthropicContentBlockType.ToolUse)]
    [InlineData("\"tool_result\"", AnthropicContentBlockType.ToolResult)]
    [InlineData("\"server_tool_use\"", AnthropicContentBlockType.ServerToolUse)]
    [InlineData("\"web_search_tool_result\"", AnthropicContentBlockType.WebSearchToolResult)]
    public void ContentBlockTypeConverter_ReadValidValue_ReturnsExpected(string json, AnthropicContentBlockType expected)
    {
        var converter = new AnthropicContentBlockTypeConverter();
        var result = Read(json, converter);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("\"unknown\"")]
    [InlineData("null")]
    public void ContentBlockTypeConverter_ReadInvalidValue_FallsBackToText(string json)
    {
        var converter = new AnthropicContentBlockTypeConverter();
        var result = Read(json, converter);
        result.Should().Be(AnthropicContentBlockType.Text);
    }

    [Theory]
    [InlineData(AnthropicContentBlockType.Text, "\"text\"")]
    [InlineData(AnthropicContentBlockType.Thinking, "\"thinking\"")]
    [InlineData(AnthropicContentBlockType.ToolUse, "\"tool_use\"")]
    [InlineData(AnthropicContentBlockType.ToolResult, "\"tool_result\"")]
    [InlineData(AnthropicContentBlockType.ServerToolUse, "\"server_tool_use\"")]
    [InlineData(AnthropicContentBlockType.WebSearchToolResult, "\"web_search_tool_result\"")]
    public void ContentBlockTypeConverter_WriteValue_ReturnsExpectedString(AnthropicContentBlockType value, string expected)
    {
        var converter = new AnthropicContentBlockTypeConverter();
        Write(value, converter).Should().Be(expected);
    }

    #endregion

    #region AnthropicStreamingEventTypeConverter

    [Theory]
    [InlineData("\"message_start\"", AnthropicStreamingEventType.MessageStart)]
    [InlineData("\"content_block_start\"", AnthropicStreamingEventType.ContentBlockStart)]
    [InlineData("\"content_block_delta\"", AnthropicStreamingEventType.ContentBlockDelta)]
    [InlineData("\"message_delta\"", AnthropicStreamingEventType.MessageDelta)]
    [InlineData("\"message_stop\"", AnthropicStreamingEventType.MessageStop)]
    [InlineData("\"content_block_stop\"", AnthropicStreamingEventType.ContentBlockStop)]
    [InlineData("\"ping\"", AnthropicStreamingEventType.Ping)]
    public void StreamingEventTypeConverter_ReadValidValue_ReturnsExpected(string json, AnthropicStreamingEventType expected)
    {
        var converter = new AnthropicStreamingEventTypeConverter();
        Read(json, converter).Should().Be(expected);
    }

    [Theory]
    [InlineData("\"unknown\"")]
    [InlineData("null")]
    public void StreamingEventTypeConverter_ReadInvalidValue_FallsBackToDefault(string json)
    {
        var converter = new AnthropicStreamingEventTypeConverter();
        Read(json, converter).Should().Be(default(AnthropicStreamingEventType));
    }

    [Fact]
    public void StreamingEventTypeConverter_WriteValue_ReturnsExpectedString()
    {
        var converter = new AnthropicStreamingEventTypeConverter();
        Write(AnthropicStreamingEventType.ContentBlockDelta, converter).Should().Be("\"content_block_delta\"");
    }

    #endregion

    #region AnthropicDeltaTypeConverter

    [Theory]
    [InlineData("\"thinking_delta\"", AnthropicDeltaType.ThinkingDelta)]
    [InlineData("\"text_delta\"", AnthropicDeltaType.TextDelta)]
    [InlineData("\"input_json_delta\"", AnthropicDeltaType.InputJsonDelta)]
    public void DeltaTypeConverter_ReadValidValue_ReturnsExpected(string json, AnthropicDeltaType expected)
    {
        var converter = new AnthropicDeltaTypeConverter();
        Read(json, converter).Should().Be(expected);
    }

    [Theory]
    [InlineData("\"unknown\"")]
    [InlineData("null")]
    public void DeltaTypeConverter_ReadInvalidValue_FallsBackToDefault(string json)
    {
        var converter = new AnthropicDeltaTypeConverter();
        Read(json, converter).Should().Be(default(AnthropicDeltaType));
    }

    [Fact]
    public void DeltaTypeConverter_WriteValue_ReturnsExpectedString()
    {
        var converter = new AnthropicDeltaTypeConverter();
        Write(AnthropicDeltaType.InputJsonDelta, converter).Should().Be("\"input_json_delta\"");
    }

    #endregion

    #region AnthropicStopReasonConverter

    [Theory]
    [InlineData("\"end_turn\"", AnthropicStopReason.EndTurn)]
    [InlineData("\"tool_use\"", AnthropicStopReason.ToolUse)]
    [InlineData("\"stop_sequence\"", AnthropicStopReason.StopSequence)]
    [InlineData("\"max_tokens\"", AnthropicStopReason.MaxTokens)]
    public void StopReasonConverter_ReadValidValue_ReturnsExpected(string json, AnthropicStopReason expected)
    {
        var converter = new AnthropicStopReasonConverter();
        Read(json, converter).Should().Be(expected);
    }

    [Theory]
    [InlineData("\"unknown\"")]
    [InlineData("null")]
    public void StopReasonConverter_ReadInvalidValue_FallsBackToDefault(string json)
    {
        var converter = new AnthropicStopReasonConverter();
        Read(json, converter).Should().Be(default(AnthropicStopReason));
    }

    [Fact]
    public void StopReasonConverter_WriteValue_ReturnsExpectedString()
    {
        var converter = new AnthropicStopReasonConverter();
        Write(AnthropicStopReason.ToolUse, converter).Should().Be("\"tool_use\"");
    }

    #endregion

    #region Enum value constants

    [Fact]
    public void AnthropicContentBlockTypeValues_AreDefined()
    {
        var values = Enum.GetValues<AnthropicContentBlockType>();
        values.Should().Contain(AnthropicContentBlockType.Text);
        values.Should().Contain(AnthropicContentBlockType.ToolUse);
        values.Should().Contain(AnthropicContentBlockType.ToolResult);
    }

    [Fact]
    public void AnthropicStreamingEventTypeValues_AreDefined()
    {
        var values = Enum.GetValues<AnthropicStreamingEventType>();
        values.Should().Contain(AnthropicStreamingEventType.MessageStart);
        values.Should().Contain(AnthropicStreamingEventType.MessageStop);
    }

    [Fact]
    public void AnthropicStopReason_ToValue_MapsCorrectly()
    {
        AnthropicStopReason.EndTurn.ToValue().Should().Be("end_turn");
        AnthropicStopReason.ToolUse.ToValue().Should().Be("tool_use");
    }

    #endregion

    private static T Read<T>(string json, JsonConverter<T> converter)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return converter.Read(ref reader, typeof(T), null!)!;
    }

    private static string Write<T>(T value, JsonConverter<T> converter)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        converter.Write(writer, value, null!);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
