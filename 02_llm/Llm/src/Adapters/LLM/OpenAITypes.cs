
namespace Api.LLM;

internal sealed class OpenAIChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAIApiMessage> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    /// <summary>
    /// 流式响应选项 — stream=true 时设置 include_usage=true,
    /// 使 OpenAI API 在最后一个 chunk 返回 usage 字段(含 cached_tokens)。
    /// </summary>
    [JsonPropertyName("stream_options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAIStreamOptions? StreamOptions { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? TopP { get; set; }

    [JsonPropertyName("frequency_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? FrequencyPenalty { get; set; }

    [JsonPropertyName("presence_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? PresencePenalty { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<OpenAITool> Tools { get; set; } = [];

    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolChoice { get; set; }

    /// <summary>
    /// 两阶段工具加载 — MCP 工具分组（只有组名+工具名，不含完整 schema）
    /// LLM 通过 ToolSearch 按需加载完整描述。null 时不序列化，真实 LLM API 忽略此字段。
    /// </summary>
    [JsonPropertyName("tool_groups")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<OpenAIToolGroup> ToolGroups { get; set; } = [];

    /// <summary>
    /// 两阶段工具加载 — 工具完整描述（第二次请求发送，响应 tool_description_request 后）
    /// </summary>
    [JsonPropertyName("tool_descriptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<OpenAITool> ToolDescriptions { get; set; } = [];

    [JsonPropertyName("reasoning_effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningEffort { get; set; }

    /// <summary>
    /// 思考模式开关 — DeepSeek V4 扩展字段,thinking:{"type":"enabled"} 开启思考模式
    /// </summary>
    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAIThinkingOptions? Thinking { get; set; }
}

/// <summary>
/// OpenAI 流式响应选项 — 控制 stream 模式下的额外数据返回。
/// 真实 API: stream_options.include_usage=true 时, 最后一个 chunk 包含 usage 字段。
/// </summary>
internal sealed class OpenAIStreamOptions
{
    [JsonPropertyName("include_usage")]
    public bool IncludeUsage { get; set; }
}

/// <summary>
/// OpenAI 消息内容 — 支持 string 或 List&lt;OpenAIContentPart&gt; 两种形态
/// 对齐 AnthropicMessageContent — 纯文本时序列化为 string，多模态时序列化为 content part 数组
/// 使用 JsonConverter 实现 AOT 兼容的多态序列化（DeepSeek vision / OpenAI vision 等多模态模型）
/// </summary>
[JsonConverter(typeof(OpenAIMessageContentConverter))]
internal sealed class OpenAIMessageContent
{
    public string? Text { get; init; }
    public List<OpenAIContentPart> Parts { get; init; } = [];

    public static implicit operator OpenAIMessageContent?(string? text) =>
        text is null ? null : new() { Text = text };

    public static implicit operator OpenAIMessageContent?(List<OpenAIContentPart> parts) =>
        new() { Parts = parts };
}

/// <summary>
/// OpenAI content part — 多模态内容块，type=text 时填 Text，type=image_url 时填 ImageUrl
/// 对齐 OpenAI Chat Completions content block 格式（DeepSeek vision 兼容）
/// </summary>
internal sealed class OpenAIContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("image_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAIImageUrl? ImageUrl { get; set; }
}

/// <summary>
/// OpenAI image_url — url 为 data:image/xxx;base64,... 内联格式或 http(s) 外链
/// </summary>
internal sealed class OpenAIImageUrl
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>detail 级别 — low/high/original/auto，DeepSeek vision 支持</summary>
    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }
}

internal sealed class OpenAIApiMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAIMessageContent? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<OpenAIToolCall> ToolCalls { get; set; } = [];

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
}

internal sealed class OpenAIChatResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public List<OpenAIChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAIUsage? Usage { get; set; }
}

/// <summary>
/// OpenAI API Usage 响应模型 — 映射到 <see cref="JoinCode.Abstractions.LLM.Chat.TokenUsage"/> 时：
/// PromptTokens → PromptTokens, CompletionTokens → CompletionTokens,
/// PromptCacheHitTokens + PromptCacheMissTokens → CacheCreationInputTokens/CacheReadInputTokens
/// </summary>
internal sealed class OpenAIUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    [JsonPropertyName("prompt_tokens_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAIPromptTokensDetails? PromptTokensDetails { get; set; }

    [JsonPropertyName("prompt_cache_hit_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PromptCacheHitTokens { get; set; }

    [JsonPropertyName("prompt_cache_miss_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PromptCacheMissTokens { get; set; }
}

internal sealed class OpenAIPromptTokensDetails
{
    [JsonPropertyName("cached_tokens")]
    public int CachedTokens { get; set; }
}

internal sealed class OpenAIChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public OpenAIApiMessage Message { get; set; } = new();

    [JsonPropertyName("delta")]
    public OpenAIApiMessage? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal sealed class OpenAIChatChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public List<OpenAIChoice> Choices { get; set; } = new();

    /// <summary>
    /// 流式最终 chunk 的 usage 字段。
    /// 真实 OpenAI API: stream_options.include_usage=true 时,
    /// 最后一个 chunk (choices 为空) 包含 usage 字段(含 prompt_tokens_details.cached_tokens)。
    /// 中间 chunk 的 usage 为 null。
    /// </summary>
    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAIUsage? Usage { get; set; }
}

internal sealed class OpenAITool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAIFunctionDefinition Function { get; set; } = new();
}

internal sealed class OpenAIFunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAIFunctionParameters? Parameters { get; set; }
}

internal sealed class OpenAIFunctionParameters : InputSchemaBase
{
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, OpenAIParameterProperty> Properties { get; set; } = [];
}

internal sealed class OpenAIParameterProperty : SchemaProperty
{
    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Enum { get; set; } = [];
}

internal sealed class OpenAIToolCall
{
    [JsonPropertyName("index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Index { get; set; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAIToolCallFunction? Function { get; set; }
}

internal sealed class OpenAIToolCallFunction
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }
}

/// <summary>
/// DeepSeek V4 思考模式选项 — thinking:{"type":"enabled"} 开启思考模式
/// </summary>
internal sealed class OpenAIThinkingOptions
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// 两阶段工具加载 — MCP 工具分组（只有组名+工具名，不含完整 schema）
/// </summary>
internal sealed class OpenAIToolGroup
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("tools")]
    public List<string> Tools { get; set; } = new();
}

/// <summary>
/// OpenAIMessageContent 的 AOT 兼容序列化转换器
/// 序列化：Text 非空 → 写字符串，Parts 非空 → 写 content part 数组
/// 反序列化：JSON 字符串 → Text，JSON 数组 → Parts
/// 对齐 AnthropicMessageContentConverter — 支持 OpenAI/DeepSeek vision 多模态 content
/// </summary>
internal sealed class OpenAIMessageContentConverter : JsonConverter<OpenAIMessageContent?>
{
    public override OpenAIMessageContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new OpenAIMessageContent { Text = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var parts = new List<OpenAIContentPart>();
            using var doc = JsonDocument.ParseValue(ref reader);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var part = element.Deserialize(NativeJsonContext.Default.OpenAIContentPart);
                if (part is not null)
                    parts.Add(part);
            }
            return new OpenAIMessageContent { Parts = parts };
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, OpenAIMessageContent? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.Text is not null)
        {
            writer.WriteStringValue(value.Text);
            return;
        }

        if (value.Parts.Count > 0)
        {
            writer.WriteStartArray();
            foreach (var part in value.Parts)
                JsonSerializer.Serialize(writer, part, NativeJsonContext.Default.OpenAIContentPart);
            writer.WriteEndArray();
            return;
        }

        writer.WriteNullValue();
    }
}
