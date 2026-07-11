namespace Api.LLM;

internal sealed class AnthropicToolReferenceBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "tool_reference";

    [JsonPropertyName("tool_name")]
    public string ToolName { get; set; } = string.Empty;
}
