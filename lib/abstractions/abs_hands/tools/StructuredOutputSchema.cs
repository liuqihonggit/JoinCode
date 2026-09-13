namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 结构化输出Schema定义 - 用于LLM输出格式约束
/// </summary>
public sealed class StructuredOutputSchema
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("schemaJson")]
    public string SchemaJson { get; init; } = string.Empty;

    [JsonPropertyName("strict")]
    public bool Strict { get; init; } = true;
}

/// <summary>
/// 结构化输出验证结果
/// </summary>
public sealed class StructuredOutputResult
{
    [JsonPropertyName("schemaName")]
    public string SchemaName { get; init; } = string.Empty;

    [JsonPropertyName("outputJson")]
    public string OutputJson { get; init; } = string.Empty;

    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    [JsonPropertyName("validationErrors")]
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();
}

/// <summary>
/// 验证错误详情
/// </summary>
public sealed class ValidationError
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
