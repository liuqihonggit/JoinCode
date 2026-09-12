namespace JoinCode.Abstractions.Tools;

public interface IJsonSchemaValidator
{
    SchemaValidationResult ValidateSchema(string schemaJson);

    SchemaValidationResult Validate(string jsonInstance, string schemaJson);
}

public sealed class SchemaValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<ValidationError> Errors { get; init; } = Array.Empty<ValidationError>();
}
