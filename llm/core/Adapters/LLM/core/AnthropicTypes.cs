
namespace Api.LLM;

internal sealed class AnthropicMessagesRequest {
    /// <summary>获取或设置模型名称。</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>获取或设置最大输出 token 数。</summary>
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;

    /// <summary>获取或设置系统消息块列表。</summary>
    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<AnthropicSystemContentBlock> System { get; set; } = [];

    /// <summary>获取或设置对话消息列表。</summary>
    [JsonPropertyName("messages")]
    public List<AnthropicMessage> Messages { get; set; } = new();

    /// <summary>获取或设置是否启用流式响应。</summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    /// <summary>获取或设置采样温度。</summary>
    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; set; }

    /// <summary>获取或设置 nucleus sampling 的 top_p 值。</summary>
    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? TopP { get; set; }

    /// <summary>获取或设置工具定义列表。</summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<AnthropicToolDefinition> Tools { get; set; } = [];

    /// <summary>获取或设置工具选择策略。</summary>
    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicToolChoice? ToolChoice { get; set; }

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
    public List<AnthropicToolDefinition> ToolDescriptions { get; set; } = [];

    /// <summary>获取或设置扩展思考配置。</summary>
    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicThinkingConfig? Thinking { get; set; }

    /// <summary>
    /// API 端上下文管理 — 对齐 TS context_management 请求参数
    /// 让 Anthropic API 在服务端自动清理工具结果，不破坏 prompt cache
    /// </summary>
    [JsonPropertyName("context_management")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicContextManagement? ContextManagement { get; set; }
}

internal sealed class AnthropicThinkingConfig {
    /// <summary>获取或设置思考配置类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "enabled";

    /// <summary>获取或设置思考预算 token 数。</summary>
    [JsonPropertyName("budget_tokens")]
    public int BudgetTokens { get; set; }
}

internal sealed class AnthropicSystemContentBlock {
    /// <summary>获取或设置内容块类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>获取或设置文本内容。</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>获取或设置缓存控制配置。</summary>
    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicCacheControl? CacheControl { get; set; }

    /// <summary>获取或设置是否为静态内容（不参与序列化）。</summary>
    [JsonIgnore]
    public bool IsStatic { get; set; } = true;
}

/// <summary>
/// Anthropic 消息内容 — 支持 string 或 List&lt;AnthropicContentBlock&gt; 两种形态
/// 使用 JsonConverter 实现 AOT 兼容的多态序列化
/// </summary>
[JsonConverter(typeof(AnthropicMessageContentConverter))]
internal sealed class AnthropicMessageContent {
    /// <summary>获取文本形态的内容。</summary>
    public string? Text { get; init; }
    /// <summary>获取内容块列表形态的内容。</summary>
    public List<AnthropicContentBlock> Blocks { get; init; } = [];

    /// <summary>获取一个值，指示当前是否为文本形态。</summary>
    public bool IsText => Text is not null;
    /// <summary>获取一个值，指示当前是否为内容块列表形态。</summary>
    public bool IsBlocks => Blocks.Count > 0;

    public static implicit operator AnthropicMessageContent?(string? text) =>
        text is null ? null : new() { Text = text };

    public static implicit operator AnthropicMessageContent?(List<AnthropicContentBlock> blocks) =>
        new() { Blocks = blocks };
}

internal sealed class AnthropicMessage {
    /// <summary>获取或设置消息角色。</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>获取或设置消息内容。</summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicMessageContent? Content { get; set; }
}

internal abstract class AnthropicContentBlock {
    /// <summary>获取或设置内容块类型。</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(AnthropicContentBlockTypeConverter))]
    public AnthropicContentBlockType Type { get; set; } = AnthropicContentBlockType.Text;

    /// <summary>获取或设置缓存控制配置。</summary>
    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicCacheControl? CacheControl { get; set; }
}

internal sealed class AnthropicTextBlock : AnthropicContentBlock {
    /// <summary>获取或设置文本内容。</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    public AnthropicTextBlock() => Type = AnthropicContentBlockType.Text;
}

internal sealed class AnthropicToolUseBlock : AnthropicContentBlock {
    /// <summary>获取或设置工具使用 ID。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>获取或设置工具名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>获取或设置工具输入参数。</summary>
    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; }

    public AnthropicToolUseBlock() => Type = AnthropicContentBlockType.ToolUse;
}

internal sealed class AnthropicToolResultBlock : AnthropicContentBlock {
    /// <summary>获取或设置对应的工具使用 ID。</summary>
    [JsonPropertyName("tool_use_id")]
    public string ToolUseId { get; set; } = string.Empty;

    /// <summary>获取或设置工具结果内容。</summary>
    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }

    /// <summary>获取或设置是否为错误结果。</summary>
    [JsonPropertyName("is_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsError { get; set; }

    public AnthropicToolResultBlock() => Type = AnthropicContentBlockType.ToolResult;
}

internal sealed class AnthropicCacheControl {
    /// <summary>获取或设置缓存控制类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "ephemeral";

    /// <summary>获取或设置缓存作用域。</summary>
    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; set; }

    /// <summary>获取或设置缓存生存时间。</summary>
    [JsonPropertyName("ttl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ttl { get; set; }
}

internal sealed class AnthropicToolDefinition {
    /// <summary>获取或设置工具类型。</summary>
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Type { get; set; }

    /// <summary>获取或设置工具名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>获取或设置工具描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>获取或设置工具输入参数 schema。</summary>
    [JsonPropertyName("input_schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicInputSchema? InputSchema { get; set; }

    /// <summary>获取或设置缓存控制配置。</summary>
    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicCacheControl? CacheControl { get; set; }

    /// <summary>获取或设置是否延迟加载工具 schema。</summary>
    [JsonPropertyName("defer_loading")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }

    /// <summary>获取或设置工具最大调用次数。</summary>
    [JsonPropertyName("max_uses")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxUses { get; set; }

    /// <summary>获取或设置允许的域名列表。</summary>
    [JsonPropertyName("allowed_domains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> AllowedDomains { get; set; } = [];

    /// <summary>获取或设置禁止的域名列表。</summary>
    [JsonPropertyName("blocked_domains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> BlockedDomains { get; set; } = [];
}

internal sealed class AnthropicInputSchema : InputSchemaBase {
    /// <summary>获取或设置 schema 属性字典。</summary>
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, AnthropicSchemaProperty> Properties { get; set; } = [];
}

internal sealed class AnthropicSchemaProperty : SchemaProperty {
    /// <summary>获取或设置枚举值列表。</summary>
    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Enum { get; set; } = [];
}

internal sealed class AnthropicMessagesResponse {
    /// <summary>获取或设置响应 ID。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>获取或设置响应类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>获取或设置消息角色。</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>获取或设置响应内容块列表。</summary>
    [JsonPropertyName("content")]
    public List<AnthropicResponseContentBlock> Content { get; set; } = new();

    /// <summary>获取或设置模型名称。</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>获取或设置停止原因。</summary>
    [JsonPropertyName("stop_reason")]
    [JsonConverter(typeof(AnthropicStopReasonConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicStopReason? StopReason { get; set; }

    /// <summary>获取或设置 token 用量统计。</summary>
    [JsonPropertyName("usage")]
    public AnthropicUsage? Usage { get; set; }
}

internal sealed class AnthropicResponseContentBlock {
    /// <summary>获取或设置内容块类型。</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(AnthropicContentBlockTypeConverter))]
    public AnthropicContentBlockType Type { get; set; } = AnthropicContentBlockType.Text;

    /// <summary>获取或设置文本内容。</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>获取或设置思考内容。</summary>
    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Thinking { get; set; }

    /// <summary>获取或设置工具使用 ID。</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    /// <summary>获取或设置工具名称。</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>获取或设置工具输入参数。</summary>
    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Input { get; set; }

    // web_search_tool_result 的 content 字段（搜索结果数组）
    /// <summary>获取或设置搜索结果内容（web_search_tool_result 专用）。</summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Content { get; set; }
}

/// <summary>
/// Anthropic API Usage 响应模型 — 映射到 <see cref="JoinCode.Abstractions.LLM.Chat.TokenUsage"/> 时：
/// InputTokens → PromptTokens, OutputTokens → CompletionTokens,
/// CacheCreationInputTokens → CacheCreationInputTokens, CacheReadInputTokens → CacheReadInputTokens
/// </summary>
internal sealed class AnthropicUsage {
    /// <summary>获取或设置输入 token 数。</summary>
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    /// <summary>获取或设置输出 token 数。</summary>
    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    /// <summary>获取或设置缓存创建输入 token 数。</summary>
    [JsonPropertyName("cache_creation_input_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CacheCreationInputTokens { get; set; }

    /// <summary>获取或设置缓存读取输入 token 数。</summary>
    [JsonPropertyName("cache_read_input_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CacheReadInputTokens { get; set; }

    /// <summary>获取或设置输出 token 明细。</summary>
    [JsonPropertyName("output_tokens_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicOutputTokensDetails? OutputTokensDetails { get; set; }
}

internal sealed class AnthropicOutputTokensDetails {
    /// <summary>获取或设置推理 token 数。</summary>
    [JsonPropertyName("reasoning_tokens")]
    public int ReasoningTokens { get; set; }
}

internal sealed class AnthropicStreamingEvent {
    /// <summary>获取或设置流式事件类型。</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(AnthropicStreamingEventTypeConverter))]
    public AnthropicStreamingEventType Type { get; set; } = default;

    /// <summary>获取或设置内容块索引。</summary>
    [JsonPropertyName("index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Index { get; set; }

    /// <summary>获取或设置消息响应（message_start 事件）。</summary>
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicMessagesResponse? Message { get; set; }

    /// <summary>获取或设置内容块（content_block_start 事件）。</summary>
    [JsonPropertyName("content_block")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicResponseContentBlock? ContentBlock { get; set; }

    /// <summary>获取或设置流式增量。</summary>
    [JsonPropertyName("delta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicStreamingDelta? Delta { get; set; }

    /// <summary>获取或设置 token 用量统计（message_delta 事件）。</summary>
    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicUsage? Usage { get; set; }
}

internal sealed class AnthropicStreamingDelta {
    /// <summary>获取或设置增量类型。</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(AnthropicDeltaTypeConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public AnthropicDeltaType? Type { get; set; }

    /// <summary>获取或设置文本增量。</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>获取或设置思考增量。</summary>
    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Thinking { get; set; }

    /// <summary>获取或设置停止原因。</summary>
    [JsonPropertyName("stop_reason")]
    [JsonConverter(typeof(AnthropicStopReasonConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicStopReason? StopReason { get; set; }

    /// <summary>获取或设置部分 JSON 增量。</summary>
    [JsonPropertyName("partial_json")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PartialJson { get; set; }
}

/// <summary>
/// API 端上下文管理 — 对齐 TS ContextManagementConfig
/// </summary>
internal sealed class AnthropicContextManagement {
    /// <summary>获取或设置上下文编辑策略列表。</summary>
    [JsonPropertyName("edits")]
    public List<AnthropicContextEditStrategy> Edits { get; set; } = new();
}

/// <summary>
/// 上下文编辑策略基类 — 对齐 TS ContextEditStrategy
/// 使用 JsonPolymorphic 实现多态序列化（AOT 兼容）
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AnthropicClearToolUsesStrategy), "clear_tool_uses_20250919")]
[JsonDerivedType(typeof(AnthropicClearThinkingStrategy), "clear_thinking_20251015")]
internal abstract class AnthropicContextEditStrategy;

/// <summary>
/// 清除工具使用记录策略 — 对齐 TS clear_tool_uses_20250919
/// </summary>
internal sealed class AnthropicClearToolUsesStrategy : AnthropicContextEditStrategy {
    /// <summary>获取或设置触发条件。</summary>
    [JsonPropertyName("trigger")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicContextTrigger? Trigger { get; set; }

    /// <summary>获取或设置保留策略。</summary>
    [JsonPropertyName("keep")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicContextKeep? Keep { get; set; }

    /// <summary>获取或设置清除工具输入的配置。</summary>
    [JsonPropertyName("clear_tool_inputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ClearToolInputs { get; set; }

    /// <summary>获取或设置排除工具列表（不清理这些工具的使用记录）。</summary>
    [JsonPropertyName("exclude_tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> ExcludeTools { get; set; } = [];

    /// <summary>获取或设置最少清除阈值。</summary>
    [JsonPropertyName("clear_at_least")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicContextTokenThreshold? ClearAtLeast { get; set; }
}

/// <summary>
/// 清除 thinking 块策略 — 对齐 TS clear_thinking_20251015
/// </summary>
internal sealed class AnthropicClearThinkingStrategy : AnthropicContextEditStrategy {
    /// <summary>获取或设置保留策略。</summary>
    [JsonPropertyName("keep")]
    public required JsonElement Keep { get; set; }
}

internal class AnthropicContextPolicyValue {
    /// <summary>获取或设置策略类型。</summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }
    /// <summary>获取或设置策略值。</summary>
    [JsonPropertyName("value")]
    public required int Value { get; set; }
}

internal sealed class AnthropicContextTrigger : AnthropicContextPolicyValue;
internal sealed class AnthropicContextKeep : AnthropicContextPolicyValue;
internal sealed class AnthropicContextTokenThreshold : AnthropicContextPolicyValue;

/// <summary>
/// Anthropic tool_choice 参数 — 替代匿名类型以满足 AOT 兼容性
/// </summary>
internal sealed class AnthropicToolChoice {
    /// <summary>获取或设置工具选择类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "auto";

    /// <summary>获取或设置指定工具名称（type 为 tool 时使用）。</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    public static readonly AnthropicToolChoice Auto = new() { Type = "auto" };
    public static readonly AnthropicToolChoice Any = new() { Type = "any" };
    public static readonly AnthropicToolChoice None = new() { Type = "none" };

    /// <summary>创建指定工具名称的选择策略。</summary>
    /// <param name="toolName">工具名称。</param>
    /// <returns>指向指定工具的 AnthropicToolChoice 实例。</returns>
    public static AnthropicToolChoice Named(string toolName) => new() { Type = "tool", Name = toolName };
}

/// <summary>
/// AnthropicMessage.Content 的 AOT 兼容序列化转换器
/// 序列化：Text → 直接写字符串，Blocks → 写数组
/// 反序列化：字符串 → Text，数组 → Blocks
/// </summary>
internal sealed class AnthropicMessageContentConverter : JsonConverter<AnthropicMessageContent?> {
    public override AnthropicMessageContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.String) {
            return new AnthropicMessageContent { Text = reader.GetString() };
        }

        if (reader.TokenType == JsonTokenType.StartArray) {
            var blocks = new List<AnthropicContentBlock>();
            using var doc = JsonDocument.ParseValue(ref reader);
            foreach (var element in doc.RootElement.EnumerateArray()) {
                var typeStr = element.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                var block = typeStr switch {
                    "text" => (AnthropicContentBlock?)element.Deserialize(AnthropicJsonContext.Default.AnthropicTextBlock),
                    "tool_use" => element.Deserialize(AnthropicJsonContext.Default.AnthropicToolUseBlock),
                    "tool_result" => element.Deserialize(AnthropicJsonContext.Default.AnthropicToolResultBlock),
                    "thinking" => element.Deserialize(AnthropicJsonContext.Default.AnthropicTextBlock),
                    _ => null
                };
                if (block is not null)
                    blocks.Add(block);
            }
            return new AnthropicMessageContent { Blocks = blocks };
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, AnthropicMessageContent? value, JsonSerializerOptions options) {
        if (value is null) {
            writer.WriteNullValue();
            return;
        }

        if (value.Text is not null) {
            writer.WriteStringValue(value.Text);
            return;
        }

        if (value.Blocks.Count > 0) {
            writer.WriteStartArray();
            foreach (var block in value.Blocks) {
                switch (block) {
                    case AnthropicTextBlock tb:
                    JsonSerializer.Serialize(writer, tb, AnthropicJsonContext.Default.AnthropicTextBlock);
                    break;
                    case AnthropicToolUseBlock tub:
                    JsonSerializer.Serialize(writer, tub, AnthropicJsonContext.Default.AnthropicToolUseBlock);
                    break;
                    case AnthropicToolResultBlock trb:
                    JsonSerializer.Serialize(writer, trb, AnthropicJsonContext.Default.AnthropicToolResultBlock);
                    break;
                    default:
                    JsonSerializer.Serialize(writer, block, AnthropicJsonContext.Default.AnthropicContentBlock);
                    break;
                }
            }
            writer.WriteEndArray();
            return;
        }

        writer.WriteNullValue();
    }
}