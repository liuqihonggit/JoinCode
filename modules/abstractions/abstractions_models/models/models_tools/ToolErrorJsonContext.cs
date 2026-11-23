namespace JoinCode.Abstractions.Tools;

[JsonSerializable(typeof(ToolErrorJson))]
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
public sealed partial class ToolErrorJsonContext : JsonSerializerContext;
