namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 结构化输出Schema定义 - 用于LLM输出格式约束
/// </summary>
public sealed class StructuredOutputSchema {
    /// <summary>获取 Schema 名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>获取 Schema 描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>获取 Schema JSON 字符串。</summary>
    [JsonPropertyName("schemaJson")]
    public string SchemaJson { get; init; } = string.Empty;

    /// <summary>获取是否严格模式。</summary>
    [JsonPropertyName("strict")]
    public bool Strict { get; init; } = true;
}

/// <summary>
/// 结构化输出验证结果
/// </summary>
public sealed class StructuredOutputResult {
    /// <summary>获取 Schema 名称。</summary>
    [JsonPropertyName("schemaName")]
    public string SchemaName { get; init; } = string.Empty;

    /// <summary>获取输出 JSON 字符串。</summary>
    [JsonPropertyName("outputJson")]
    public string OutputJson { get; init; } = string.Empty;

    /// <summary>获取是否有效。</summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    /// <summary>获取验证错误列表。</summary>
    [JsonPropertyName("validationErrors")]
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();
}

/// <summary>
/// 验证错误详情
/// </summary>
public sealed class ValidationError {
    /// <summary>获取错误路径。</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    /// <summary>获取错误消息。</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}