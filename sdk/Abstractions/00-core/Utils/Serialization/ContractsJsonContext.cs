namespace JoinCode.Abstractions.Utils;

[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Tools.StructuredOutputSchema))]
[JsonSerializable(typeof(Tools.StructuredOutputResult))]
[JsonSerializable(typeof(Tools.ValidationError))]
[JsonSerializable(typeof(List<Tools.ValidationError>))]
[JsonSerializable(typeof(Tools.ToolSchema))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
public sealed partial class ContractsJsonContext : JsonSerializerContext;
