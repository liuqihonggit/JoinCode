
namespace McpClient;

[JsonSerializable(typeof(McpToolsListResponse))]
[JsonSerializable(typeof(OAuth2TokenResponse))]
[JsonSerializable(typeof(ToolResult))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(McpServerDisabledState))]
[JsonSerializable(typeof(McpbManifest))]
[JsonSerializable(typeof(McpbCacheMetadata))]
[JsonSerializable(typeof(List<McpRegistryEntry>))]
[JsonSerializable(typeof(McpRegistryServerDetail))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
internal partial class McpClientJsonContext : JsonSerializerContext;
