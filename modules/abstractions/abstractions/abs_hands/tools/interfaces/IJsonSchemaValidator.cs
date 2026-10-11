namespace JoinCode.Abstractions.Tools;

public interface IJsonSchemaValidator {
    /// <summary>校验 JSON Schema 格式是否合法。</summary>
    SchemaValidationResult ValidateSchema(string schemaJson);

    /// <summary>校验 JSON 实例是否符合 Schema。</summary>
    SchemaValidationResult Validate(string jsonInstance, string schemaJson);
}

public sealed class SchemaValidationResult {
    /// <summary>获取是否合法。</summary>
    public bool IsValid { get; init; }
    /// <summary>获取校验错误列表。</summary>
    public IReadOnlyList<ValidationError> Errors { get; init; } = Array.Empty<ValidationError>();
}