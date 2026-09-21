namespace Api.LLM;

internal sealed class AnthropicToolReferenceBlock {
    /// <summary>获取或设置类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "tool_reference";

    /// <summary>获取或设置工具名称。</summary>
    [JsonPropertyName("tool_name")]
    public string ToolName { get; set; } = string.Empty;
}