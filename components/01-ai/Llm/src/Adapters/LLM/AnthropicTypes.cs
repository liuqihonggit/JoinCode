
namespace Api.LLM;

internal sealed class AnthropicMessagesRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;

    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AnthropicSystemContentBlock>? System { get; set; }

    [JsonPropertyName("messages")]
    public List<AnthropicMessage> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? TopP { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AnthropicToolDefinition>? Tools { get; set; }

    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ToolChoice { get; set; }

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

internal sealed class AnthropicThinkingConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "enabled";

    [JsonPropertyName("budget_tokens")]
    public int BudgetTokens { get; set; }
}

internal sealed class AnthropicSystemContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicCacheControl? CacheControl { get; set; }

    [JsonIgnore]
    public bool IsStatic { get; set; } = true;
}

internal sealed class AnthropicMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public object? Content { get; set; }
}

internal abstract class AnthropicContentBlock
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(AnthropicContentBlockTypeConverter))]
    public AnthropicContentBlockType Type { get; set; } = AnthropicContentBlockType.Text;

    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicCacheControl? CacheControl { get; set; }
}

internal sealed class AnthropicTextBlock : AnthropicContentBlock
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    public AnthropicTextBlock() => Type = AnthropicContentBlockType.Text;
}

internal sealed class AnthropicToolUseBlock : AnthropicContentBlock
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public object? Input { get; set; }

    public AnthropicToolUseBlock() => Type = AnthropicContentBlockType.ToolUse;
}

internal sealed class AnthropicToolResultBlock : AnthropicContentBlock
{
    [JsonPropertyName("tool_use_id")]
    public string ToolUseId { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public object? Content { get; set; }

    [JsonPropertyName("is_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsError { get; set; }

    public AnthropicToolResultBlock() => Type = AnthropicContentBlockType.ToolResult;
}

internal sealed class AnthropicCacheControl
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "ephemeral";

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; set; }

    [JsonPropertyName("ttl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ttl { get; set; }
}

internal sealed class AnthropicToolDefinition
{
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Type { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("input_schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicInputSchema? InputSchema { get; set; }

    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicCacheControl? CacheControl { get; set; }

    [JsonPropertyName("defer_loading")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }

    [JsonPropertyName("max_uses")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxUses { get; set; }

    [JsonPropertyName("allowed_domains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AllowedDomains { get; set; }

    [JsonPropertyName("blocked_domains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? BlockedDomains { get; set; }
}

internal sealed class AnthropicInputSchema : InputSchemaBase
{
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, AnthropicSchemaProperty>? Properties { get; set; }
}

internal sealed class AnthropicSchemaProperty : SchemaProperty
{
    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; set; }
}

internal sealed class AnthropicMessagesResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public List<AnthropicResponseContentBlock> Content { get; set; } = new();

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("stop_reason")]
    [JsonConverter(typeof(AnthropicStopReasonConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicStopReason? StopReason { get; set; }

    [JsonPropertyName("usage")]
    public AnthropicUsage? Usage { get; set; }
}

internal sealed class AnthropicResponseContentBlock
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(AnthropicContentBlockTypeConverter))]
    public AnthropicContentBlockType Type { get; set; } = AnthropicContentBlockType.Text;

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Thinking { get; set; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Input { get; set; }

    // web_search_tool_result 的 content 字段（搜索结果数组）
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Content { get; set; }
}

/// <summary>
/// Anthropic API Usage 响应模型 — 映射到 <see cref="JoinCode.Abstractions.LLM.Chat.TokenUsage"/> 时：
/// InputTokens → PromptTokens, OutputTokens → CompletionTokens,
/// CacheCreationInputTokens → CacheCreationInputTokens, CacheReadInputTokens → CacheReadInputTokens
/// </summary>
internal sealed class AnthropicUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("cache_creation_input_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CacheCreationInputTokens { get; set; }

    [JsonPropertyName("cache_read_input_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CacheReadInputTokens { get; set; }

    [JsonPropertyName("output_tokens_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicOutputTokensDetails? OutputTokensDetails { get; set; }
}

internal sealed class AnthropicOutputTokensDetails
{
    [JsonPropertyName("reasoning_tokens")]
    public int ReasoningTokens { get; set; }
}

internal sealed class AnthropicStreamingEvent
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(AnthropicStreamingEventTypeConverter))]
    public AnthropicStreamingEventType Type { get; set; } = default;

    [JsonPropertyName("index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Index { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicMessagesResponse? Message { get; set; }

    [JsonPropertyName("content_block")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicResponseContentBlock? ContentBlock { get; set; }

    [JsonPropertyName("delta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicStreamingDelta? Delta { get; set; }

    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicUsage? Usage { get; set; }
}

internal sealed class AnthropicStreamingDelta
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(AnthropicDeltaTypeConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public AnthropicDeltaType? Type { get; set; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Thinking { get; set; }

    [JsonPropertyName("stop_reason")]
    [JsonConverter(typeof(AnthropicStopReasonConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicStopReason? StopReason { get; set; }

    [JsonPropertyName("partial_json")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PartialJson { get; set; }
}

/// <summary>
/// API 端上下文管理 — 对齐 TS ContextManagementConfig
/// </summary>
internal sealed class AnthropicContextManagement
{
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
internal sealed class AnthropicClearToolUsesStrategy : AnthropicContextEditStrategy
{
    [JsonPropertyName("trigger")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicContextTrigger? Trigger { get; set; }

    [JsonPropertyName("keep")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicContextKeep? Keep { get; set; }

    [JsonPropertyName("clear_tool_inputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ClearToolInputs { get; set; }

    [JsonPropertyName("exclude_tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ExcludeTools { get; set; }

    [JsonPropertyName("clear_at_least")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicContextTokenThreshold? ClearAtLeast { get; set; }
}

/// <summary>
/// 清除 thinking 块策略 — 对齐 TS clear_thinking_20251015
/// </summary>
internal sealed class AnthropicClearThinkingStrategy : AnthropicContextEditStrategy
{
    [JsonPropertyName("keep")]
    public required object Keep { get; set; }
}

internal class AnthropicContextPolicyValue
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }
    [JsonPropertyName("value")]
    public required int Value { get; set; }
}

internal sealed class AnthropicContextTrigger : AnthropicContextPolicyValue;
internal sealed class AnthropicContextKeep : AnthropicContextPolicyValue;
internal sealed class AnthropicContextTokenThreshold : AnthropicContextPolicyValue;
