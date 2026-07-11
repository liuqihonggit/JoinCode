
namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具信息
/// </summary>
public class ToolInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("inputSchema")]
    public ToolSchema InputSchema { get; init; } = new();

    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolAnnotations? Annotations { get; init; }
}

/// <summary>
/// 工具注解
/// </summary>
public class ToolAnnotations
{
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("readOnlyHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReadOnlyHint { get; init; }

    [JsonPropertyName("destructiveHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DestructiveHint { get; init; }

    [JsonPropertyName("nonConcurrentHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NonConcurrentHint { get; init; }

    [JsonPropertyName("confirm")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Confirm { get; init; }
}

/// <summary>
/// 工具参数模式
/// </summary>
public class ToolSchema
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, ToolSchemaProperty> Properties { get; init; } = new();

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Required { get; init; }
}

/// <summary>
/// 工具模式属性
/// </summary>
public class ToolSchemaProperty
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "string";

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; init; }

    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Default { get; init; }

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolSchemaProperty? Items { get; init; }
}

/// <summary>
/// 工具调用请求
/// </summary>
public class ToolCallRequest
{
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, JsonElement> Arguments { get; set; } = new();
}

/// <summary>
/// 工具内容
/// </summary>
public class ToolContent
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(ToolContentTypeJsonConverter))]
    public ToolContentType Type { get; init; } = ToolContentType.Text;

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Data { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }
}

/// <summary>
/// 工具调用结果
/// </summary>
public class ToolResult
{
    [JsonPropertyName("content")]
    public List<ToolContent> Content { get; init; } = new();

    [JsonPropertyName("isError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsError { get; init; }

    /// <summary>
    /// 工具调用请求（内部使用，不序列化）
    /// </summary>
    [JsonIgnore]
    public ToolCallRequest? ToolCall { get; set; }

    /// <summary>
    /// 结构化 Patch 数据 — 对齐 TS FileEditOutput.structuredPatch
    /// 不序列化到 JSON，仅在进程内传递给 UI 渲染
    /// </summary>
    [JsonIgnore]
    public StructuredPatchHunk[]? StructuredPatch { get; set; }

    /// <summary>
    /// 上下文修改器 — 对齐 TS ToolResult.contextModifier
    /// 仅对非并发安全工具有效，允许工具执行后动态修改会话上下文
    /// 如：SkillTool 修改 allowedTools/model/effort
    /// </summary>
    [JsonIgnore]
    public Action<ToolUseContext>? ContextModifier { get; set; }

    /// <summary>
    /// 注入消息 — 对齐 TS SkillTool newMessages
    /// inline 技能返回技能 prompt 作为 user message，LLM 自行执行
    /// ChatService 在处理工具结果后，将这些消息追加到对话历史
    /// </summary>
    [JsonIgnore]
    public List<LLM.Chat.ApiMessage>? InjectedMessages { get; set; }

    /// <summary>
    /// fork 技能进度消息 — 对齐 TS SkillTool onProgress
    /// fork 模式执行技能时，子智能体的工具调用进度（ToolCallStart/ToolCallEnd）
    /// TS 通过 renderToolUseProgressMessage 渲染，C# 通过此属性传递给 UI 层
    /// </summary>
    [JsonIgnore]
    public List<SkillProgressMessage>? SkillProgressMessages { get; set; }

    /// <summary>
    /// 是否包含图片输出 — 对齐 TS BashTool isImageOutput
    /// 当 stdout 为 Data URI 格式图片时为 true，UI 层显示提示文本而非 base64 数据
    /// </summary>
    [JsonIgnore]
    public bool IsImage { get; set; }

    /// <summary>
    /// 获取文本内容
    /// </summary>
    public string GetTextContent()
    {
        return string.Join("\n", Content
            .Where(c => c.Type == ToolContentType.Text && !string.IsNullOrEmpty(c.Text))
            .Select(c => c.Text).ToArray());
    }
}

/// <summary>
/// 工具注册事件参数
/// </summary>
public class ToolRegisteredEventArgs : EventArgs
{
    public required string ToolName { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// 工具注销事件参数
/// </summary>
public class ToolUnregisteredEventArgs : EventArgs
{
    public required string ToolName { get; init; }
}

/// <summary>
/// 扫描到的工具信息
/// </summary>
public sealed class ScannedTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ToolSchema InputSchema { get; init; }
    public required ToolHandler Handler { get; init; }
}

/// <summary>
/// MCP工具调用结果构建器
/// </summary>
public sealed class McpResultBuilder
{
    private readonly List<ToolContent> _content = new();
    private bool _isError;

    public static McpResultBuilder Success() => new();

    public static McpResultBuilder Error() => new() { _isError = true };

    public McpResultBuilder WithText(string text)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Text, Text = text });
        return this;
    }

    /// <summary>
    /// 添加图片内容 — 对齐 TS convertResultContentToContentBlocks image 路径
    /// </summary>
    public McpResultBuilder WithImage(string base64Data, string mediaType)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Image, Data = base64Data, MimeType = mediaType });
        return this;
    }

    /// <summary>
    /// 添加二进制内容引用 — 对齐 TS persistBlobToTextBlock 写盘路径
    /// </summary>
    public McpResultBuilder WithBinary(string base64Data, string mimeType)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Resource, Data = base64Data, MimeType = mimeType });
        return this;
    }

    public McpResultBuilder WithError(string errorMessage)
    {
        _isError = true;
        _content.Clear();
        _content.Add(new ToolContent { Type = ToolContentType.Text, Text = errorMessage });
        return this;
    }

    public ToolResult Build()
    {
        return new ToolResult
        {
            Content = _content,
            IsError = _isError
        };
    }
}

/// <summary>
/// fork 技能进度消息 — 对齐 TS SkillTool onProgress
/// 记录子智能体执行技能时的工具调用进度
/// TS 仅在消息包含 tool_use/tool_result 时触发 onProgress
/// </summary>
public sealed class SkillProgressMessage
{
    /// <summary>
    /// 进度类型
    /// </summary>
    public required SkillProgressType Type { get; init; }

    /// <summary>
    /// 工具名称
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// 工具调用序号
    /// </summary>
    public int? ToolCallNumber { get; init; }

    /// <summary>
    /// 工具调用是否成功（仅 ToolCallEnd 时有意义）
    /// </summary>
    public bool ToolSucceeded { get; init; }
}

/// <summary>
/// fork 技能进度类型 — 对齐 TS SkillTool Progress.type
/// </summary>
public enum SkillProgressType
{
    /// <summary>工具调用开始 — 对齐 TS tool_use</summary>
    [EnumValue("tool_call_start")] ToolCallStart,
    /// <summary>工具调用结束 — 对齐 TS tool_result</summary>
    [EnumValue("tool_call_end")] ToolCallEnd,
}
