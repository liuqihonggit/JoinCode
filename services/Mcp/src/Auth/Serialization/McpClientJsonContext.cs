
namespace McpClient;

[JsonSerializable(typeof(McpToolsListResponse))]
[JsonSerializable(typeof(global::JoinCode.Abstractions.Models.OAuth.OAuth2TokenResponse))]
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
[JsonSerializable(typeof(global::McpToolDispatch.McpConnectionStateData))]
[JsonSerializable(typeof(global::McpToolDispatch.McpConnectionEntry))]
[JsonSerializable(typeof(List<global::McpToolDispatch.McpConnectionEntry>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
internal partial class McpClientJsonContext : JsonSerializerContext;
