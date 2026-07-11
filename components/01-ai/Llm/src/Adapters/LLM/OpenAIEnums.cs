namespace Api.LLM;

/// <summary>
/// OpenAI API 完成原因
/// </summary>
public enum OpenAIFinishReason
{
    [EnumValue("stop")] Stop,
    [EnumValue("length")] Length,
    [EnumValue("tool_calls")] ToolCalls,
    [EnumValue("content_filter")] ContentFilter
}
