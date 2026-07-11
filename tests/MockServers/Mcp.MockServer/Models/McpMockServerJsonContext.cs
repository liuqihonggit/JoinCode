namespace Mcp.MockServer.Models;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpMockServerConfig))]
[JsonSerializable(typeof(McpToolDefinition))]
[JsonSerializable(typeof(List<McpToolDefinition>))]
[JsonSerializable(typeof(JsonRpcResponse))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(ServerInfo))]
[JsonSerializable(typeof(ServerCapabilities))]
[JsonSerializable(typeof(ToolsListResult))]
[JsonSerializable(typeof(ToolInfo))]
[JsonSerializable(typeof(ToolsCallResult))]
[JsonSerializable(typeof(ToolContent))]
[JsonSerializable(typeof(ResourcesListResult))]
[JsonSerializable(typeof(ResourcesReadResult))]
[JsonSerializable(typeof(PromptsListResult))]
[JsonSerializable(typeof(PromptsGetResult))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public partial class McpMockServerJsonContext : JsonSerializerContext;
