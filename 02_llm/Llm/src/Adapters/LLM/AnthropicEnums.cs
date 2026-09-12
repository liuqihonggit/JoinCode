namespace Api.LLM;

/// <summary>
/// Anthropic API 内容块类型
/// </summary>
public enum AnthropicContentBlockType
{
    [EnumValue("text")] Text,
    [EnumValue("thinking")] Thinking,
    [EnumValue("tool_use")] ToolUse,
    [EnumValue("tool_result")] ToolResult,
    [EnumValue("server_tool_use")] ServerToolUse,
    [EnumValue("web_search_tool_result")] WebSearchToolResult
}

/// <summary>
/// Anthropic API 流式事件类型
/// </summary>
public enum AnthropicStreamingEventType
{
    [EnumValue("message_start")] MessageStart,
    [EnumValue("content_block_start")] ContentBlockStart,
    [EnumValue("content_block_delta")] ContentBlockDelta,
    [EnumValue("message_delta")] MessageDelta,
    [EnumValue("message_stop")] MessageStop,
    [EnumValue("content_block_stop")] ContentBlockStop,
    [EnumValue("ping")] Ping
}

/// <summary>
/// Anthropic API 流式 Delta 类型
/// </summary>
public enum AnthropicDeltaType
{
    [EnumValue("thinking_delta")] ThinkingDelta,
    [EnumValue("text_delta")] TextDelta,
    [EnumValue("input_json_delta")] InputJsonDelta
}

/// <summary>
/// Anthropic API 停止原因
/// </summary>
public enum AnthropicStopReason
{
    [EnumValue("end_turn")] EndTurn,
    [EnumValue("tool_use")] ToolUse,
    [EnumValue("stop_sequence")] StopSequence,
    [EnumValue("max_tokens")] MaxTokens
}

/// <summary>
/// AOT 兼容的 AnthropicContentBlockType JSON 转换器
/// </summary>
public sealed class AnthropicContentBlockTypeConverter : JsonConverter<AnthropicContentBlockType>
{
    public override AnthropicContentBlockType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return AnthropicContentBlockTypeExtensions.FromValue(value) ?? AnthropicContentBlockType.Text;
    }

    public override void Write(Utf8JsonWriter writer, AnthropicContentBlockType value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToValue());
}

/// <summary>
/// AOT 兼容的 AnthropicStreamingEventType JSON 转换器
/// </summary>
public sealed class AnthropicStreamingEventTypeConverter : JsonConverter<AnthropicStreamingEventType>
{
    public override AnthropicStreamingEventType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return AnthropicStreamingEventTypeExtensions.FromValue(value) ?? default;
    }

    public override void Write(Utf8JsonWriter writer, AnthropicStreamingEventType value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToValue());
}

/// <summary>
/// AOT 兼容的 AnthropicDeltaType JSON 转换器
/// </summary>
public sealed class AnthropicDeltaTypeConverter : JsonConverter<AnthropicDeltaType>
{
    public override AnthropicDeltaType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return AnthropicDeltaTypeExtensions.FromValue(value) ?? default;
    }

    public override void Write(Utf8JsonWriter writer, AnthropicDeltaType value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToValue());
}

/// <summary>
/// AOT 兼容的 AnthropicStopReason JSON 转换器
/// </summary>
public sealed class AnthropicStopReasonConverter : JsonConverter<AnthropicStopReason>
{
    public override AnthropicStopReason Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return AnthropicStopReasonExtensions.FromValue(value) ?? default;
    }

    public override void Write(Utf8JsonWriter writer, AnthropicStopReason value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToValue());
}
