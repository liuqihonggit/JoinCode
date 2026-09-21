
namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具信息
/// </summary>
public class ToolInfo {
    /// <summary>获取工具名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>获取工具描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>获取输入参数模式。</summary>
    [JsonPropertyName("inputSchema")]
    public ToolSchema InputSchema { get; init; } = new();

    /// <summary>获取工具注解。</summary>
    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolAnnotations? Annotations { get; init; }

    /// <summary>获取工具分类。</summary>
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; init; }

    /// <summary>获取工具组名。</summary>
    [JsonPropertyName("groupName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupName { get; init; }
}

/// <summary>
/// 工具注解
/// </summary>
public class ToolAnnotations {
    /// <summary>获取工具标题。</summary>
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    /// <summary>获取是否只读提示。</summary>
    [JsonPropertyName("readOnlyHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReadOnlyHint { get; init; }

    /// <summary>获取是否破坏性提示。</summary>
    [JsonPropertyName("destructiveHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DestructiveHint { get; init; }

    /// <summary>获取是否非并发提示。</summary>
    [JsonPropertyName("nonConcurrentHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NonConcurrentHint { get; init; }

    /// <summary>获取是否需要确认。</summary>
    [JsonPropertyName("confirm")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Confirm { get; init; }
}

/// <summary>
/// 工具参数模式
/// </summary>
public class ToolSchema {
    /// <summary>获取模式类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";

    /// <summary>获取属性字典。</summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, ToolSchemaProperty> Properties { get; init; } = new();

    /// <summary>获取必填属性名列表。</summary>
    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string> Required { get; init; } = [];
}

/// <summary>
/// 工具模式属性
/// </summary>
public class ToolSchemaProperty {
    /// <summary>获取属性类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "string";

    /// <summary>获取属性描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>获取枚举值列表。</summary>
    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
#pragma warning disable JCC11002
    public List<string>? Enum { get; init; }
#pragma warning restore JCC11002

    /// <summary>获取默认值。</summary>
    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Default { get; init; }

    /// <summary>获取数组项的模式。</summary>
    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolSchemaProperty? Items { get; init; }
}

/// <summary>
/// 工具调用请求
/// </summary>
public class ToolCallRequest {
    /// <summary>获取或设置工具名称。</summary>
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    /// <summary>获取或设置调用参数。</summary>
    [JsonPropertyName("arguments")]
    public Dictionary<string, JsonElement> Arguments { get; set; } = new();
}

/// <summary>
/// 工具内容
/// </summary>
public class ToolContent {
    /// <summary>获取内容类型。</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(ToolContentTypeJsonConverter))]
    public ToolContentType Type { get; init; } = ToolContentType.Text;

    /// <summary>获取文本内容。</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    /// <summary>获取二进制数据（Base64）。</summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Data { get; init; }

    /// <summary>获取 MIME 类型。</summary>
    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }
}

/// <summary>
/// 工具调用结果
/// </summary>
public sealed record ToolResult {
    /// <summary>获取内容列表。</summary>
    [JsonPropertyName("content")]
    public List<ToolContent> Content { get; init; } = new();

    /// <summary>获取是否为错误结果。</summary>
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

    /// <summary>获取首个非空文本内容。</summary>
    public string? GetFirstText() => Content.FirstOrDefault(c => !string.IsNullOrEmpty(c.Text))?.Text;

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
    /// 工具执行实体元数据 — Handler 可选填充，用于回填子类 Entity 特有字段
    /// CompleteExecutionEntity 根据此列表回填 BashProcessEntity.ExitCode / WebFetchEntity.HttpStatusCode 等
    /// </summary>
    [JsonIgnore]
    public List<EntityMetadataEntry>? EntityMetadata { get; set; }

    /// <summary>
    /// 结构化诊断信息 — 工具失败时填充，GUI 可根据 Reason/Details/Suggestions 分区域渲染。
    /// 成功时为 null。不序列化到 LLM（LLM 通过 Content 文本接收错误信息）。
    /// </summary>
    [JsonIgnore]
    public ToolDiagnostic? Diagnostic { get; set; }

    /// <summary>
    /// 权限决策结果 — PendingConfirmation 时由上层 ToolExecutionHandler 触发 IPermissionConfirmationHandler.Confirm。
    /// 默认 Allowed,表示工具已正常执行。不序列化到 LLM。
    /// </summary>
    [JsonIgnore]
    public PermissionDecision PermissionDecision { get; set; } = PermissionDecision.Allowed;

    /// <summary>
    /// 权限确认提示 — PermissionDecision 为 PendingConfirmation 时填充,传给 IPermissionConfirmationHandler.Confirm
    /// </summary>
    [JsonIgnore]
    public string? ConfirmationPrompt { get; set; }

    /// <summary>
    /// 权限确认规则内容 — WebFetch 等 domain:hostname 格式,用于域名级白名单持久化
    /// </summary>
    [JsonIgnore]
    public string? PermissionRuleContent { get; set; }

    /// <summary>
    /// 获取文本内容
    /// </summary>
    public string GetTextContent() {
        return string.Join("\n", Content
            .Where(c => c.Type == ToolContentType.Text && !string.IsNullOrEmpty(c.Text))
            .Select(c => c.Text).ToArray());
    }
}

/// <summary>
/// 工具注册事件参数
/// </summary>
public class ToolRegisteredEventArgs : EventArgs {
    /// <summary>获取工具名称。</summary>
    public required string ToolName { get; init; }
    /// <summary>获取工具描述。</summary>
    public required string Description { get; init; }
}

/// <summary>
/// 工具注销事件参数
/// </summary>
public class ToolUnregisteredEventArgs : EventArgs {
    /// <summary>获取工具名称。</summary>
    public required string ToolName { get; init; }
}

/// <summary>
/// 扫描到的工具信息
/// </summary>
public sealed class ScannedTool {
    /// <summary>获取工具名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取工具描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取输入参数模式。</summary>
    public required ToolSchema InputSchema { get; init; }
    /// <summary>获取工具处理器。</summary>
    public required ToolHandler Handler { get; init; }
}

/// <summary>
/// fork 技能进度消息 — 对齐 TS SkillTool onProgress
/// 记录子智能体执行技能时的工具调用进度
/// TS 仅在消息包含 tool_use/tool_result 时触发 onProgress
/// </summary>
public sealed class SkillProgressMessage {
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
public enum SkillProgressType {
    /// <summary>工具调用开始 — 对齐 TS tool_use</summary>
    [EnumValue("tool_call_start")] ToolCallStart,
    /// <summary>工具调用结束 — 对齐 TS tool_result</summary>
    [EnumValue("tool_call_end")] ToolCallEnd,
}

/// <summary>
/// 工具执行实体元数据条目 — AOT 安全的 key-value 对，用于回填子类 Entity 特有字段
/// </summary>
public sealed record EntityMetadataEntry {
    /// <summary>获取元数据键名。</summary>
    public required string Key { get; init; }
    /// <summary>获取整数值。</summary>
    public int? IntValue { get; init; }
    /// <summary>获取长整数值。</summary>
    public long? LongValue { get; init; }
    /// <summary>获取字符串值。</summary>
    public string? StringValue { get; init; }
    /// <summary>获取布尔值。</summary>
    public bool? BoolValue { get; init; }

    /// <summary>创建整型元数据条目。</summary>
    /// <param name="key">键名。</param>
    /// <param name="value">整数值。</param>
    public static EntityMetadataEntry Int(string key, int value) => new() { Key = key, IntValue = value };
    /// <summary>创建长整型元数据条目。</summary>
    /// <param name="key">键名。</param>
    /// <param name="value">长整数值。</param>
    public static EntityMetadataEntry Long(string key, long value) => new() { Key = key, LongValue = value };
    /// <summary>创建字符串元数据条目。</summary>
    /// <param name="key">键名。</param>
    /// <param name="value">字符串值。</param>
    public static EntityMetadataEntry String(string key, string value) => new() { Key = key, StringValue = value };
    /// <summary>创建布尔元数据条目。</summary>
    /// <param name="key">键名。</param>
    /// <param name="value">布尔值。</param>
    public static EntityMetadataEntry Bool(string key, bool value) => new() { Key = key, BoolValue = value };
}
