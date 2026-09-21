
namespace Api.LLM;

/// <summary>
/// Responses API 请求 — OpenAI/DeepSeek Responses API 格式(POST /responses)
/// 用 input + instructions 而非 messages,支持 reasoning effort
/// </summary>
internal sealed class ResponsesRequest {
    /// <summary>获取或设置模型标识。</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 输入 — 字符串或输入 item 列表(JsonElement 支持联合类型,AOT 友好)
    /// </summary>
    [JsonPropertyName("input")]
    public JsonElement Input { get; set; }

    /// <summary>获取或设置指令文本。</summary>
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; set; }

    /// <summary>获取或设置是否启用流式响应。</summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    /// <summary>获取或设置采样温度。</summary>
    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; set; }

    /// <summary>获取或设置核采样概率阈值。</summary>
    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? TopP { get; set; }

    /// <summary>获取或设置最大输出 Token 数。</summary>
    [JsonPropertyName("max_output_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputTokens { get; set; }

    /// <summary>获取或设置工具列表。</summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ResponsesTool> Tools { get; set; } = [];

    /// <summary>获取或设置工具选择策略。</summary>
    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolChoice { get; set; }

    /// <summary>
    /// 两阶段工具加载 — MCP 工具分组（只有组名+工具名，不含完整 schema）
    /// </summary>
    [JsonPropertyName("tool_groups")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<OpenAIToolGroup> ToolGroups { get; set; } = [];

    /// <summary>
    /// 两阶段工具加载 — 工具完整描述（第二次请求发送，响应 tool_description_request 后）
    /// </summary>
    [JsonPropertyName("tool_descriptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ResponsesTool> ToolDescriptions { get; set; } = [];

    /// <summary>获取或设置推理配置。</summary>
    [JsonPropertyName("reasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResponsesReasoning? Reasoning { get; set; }
}

internal sealed class ResponsesReasoning {
    /// <summary>获取或设置推理强度。</summary>
    [JsonPropertyName("effort")]
    public string Effort { get; set; } = string.Empty;
}

internal sealed class ResponsesTool {
    /// <summary>获取或设置工具类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    /// <summary>获取或设置工具名称。</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>获取或设置工具描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>获取或设置工具参数 schema。</summary>
    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Parameters { get; set; }
}

/// <summary>
/// Responses API 响应 — output 数组(message/reasoning/function_call items)+ output_text 便捷字段
/// </summary>
internal sealed class ResponsesResponse {
    /// <summary>获取或设置响应标识。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>获取或设置对象类型。</summary>
    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    /// <summary>获取或设置模型标识。</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>获取或设置响应状态。</summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; }

    /// <summary>获取或设置输出 item 列表。</summary>
    [JsonPropertyName("output")]
    public List<ResponsesOutputItem> Output { get; set; } = new();

    /// <summary>
    /// 输出文本便捷字段 —  assistant 文本内容拼接
    /// </summary>
    [JsonPropertyName("output_text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputText { get; set; }

    /// <summary>获取或设置用量统计。</summary>
    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResponsesUsage? Usage { get; set; }
}

/// <summary>
/// Responses API 输出 item — type 为 message/reasoning/function_call
/// </summary>
internal sealed class ResponsesOutputItem {
    /// <summary>获取或设置 item 类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>获取或设置角色。</summary>
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    /// <summary>获取或设置内容列表。</summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ResponsesContent> Content { get; set; } = [];

    /// <summary>获取或设置工具名称。</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>获取或设置工具调用参数。</summary>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }

    /// <summary>获取或设置工具调用标识。</summary>
    [JsonPropertyName("call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallId { get; set; }
}

internal sealed class ResponsesContent {
    /// <summary>获取或设置内容类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>获取或设置文本内容。</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
}

internal sealed class ResponsesUsage {
    /// <summary>获取或设置输入 Token 数。</summary>
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    /// <summary>获取或设置输出 Token 数。</summary>
    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    /// <summary>获取或设置输入 Token 详情。</summary>
    [JsonPropertyName("input_tokens_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResponsesTokenDetails? InputTokensDetails { get; set; }

    /// <summary>获取或设置输出 Token 详情。</summary>
    [JsonPropertyName("output_tokens_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResponsesTokenDetails? OutputTokensDetails { get; set; }
}

internal sealed class ResponsesTokenDetails {
    /// <summary>获取或设置缓存命中 Token 数。</summary>
    [JsonPropertyName("cached_tokens")]
    public int CachedTokens { get; set; }

    /// <summary>获取或设置推理 Token 数。</summary>
    [JsonPropertyName("reasoning_tokens")]
    public int ReasoningTokens { get; set; }
}