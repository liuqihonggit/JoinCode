namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具错误 JSON DTO — 所有工具执行错误统一序列化为此格式，放入 ToolResult.Content[0].Text。
/// AI 可直接解析 JSON 识别错误类型、原因、详情和建议，无需从纯文本中猜测。
/// </summary>
public sealed record ToolErrorJson {
    /// <summary>错误类型 — ToolErrorType 枚举的字符串值（如 "parameter_missing"）</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>工具名称 — 触发错误的工具名（如 "bash"、"read"）</summary>
    [JsonPropertyName("tool")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tool { get; init; }

    /// <summary>错误消息 — 人类可读的错误描述</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>结构化详情 — 键值对列表（行号、参数名、pattern 等）</summary>
    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<DiagnosticDetail> Details { get; init; } = [];

    /// <summary>可操作建议 — AI 可据此决定下一步操作</summary>
    [JsonPropertyName("suggestions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string> Suggestions { get; init; } = [];

    /// <summary>是否可重试 — true 表示 AI 可重试此工具调用</summary>
    [JsonPropertyName("retryable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Retryable { get; init; }
}
