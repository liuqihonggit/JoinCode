
namespace McpToolHandlers;

[JsonSourceGenerationOptions(WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public partial class McpToolHandlersJsonContext : JsonSerializerContext;
