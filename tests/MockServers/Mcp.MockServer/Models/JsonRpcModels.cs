namespace Mcp.MockServer.Models;

/// <summary>
/// MCP JSON-RPC 响应模型
/// </summary>
public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public JsonElement Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }
}

public sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// initialize 响应结果
/// </summary>
public sealed class InitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "";

    [JsonPropertyName("serverInfo")]
    public Implementation ServerInfo { get; set; } = new();

    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();
}

public sealed class ServerCapabilities
{
    [JsonPropertyName("tools")]
    public JsonElement Tools { get; set; }

    [JsonPropertyName("resources")]
    public JsonElement Resources { get; set; }

    [JsonPropertyName("prompts")]
    public JsonElement Prompts { get; set; }
}

/// <summary>
/// tools/list 响应结果
/// </summary>
public sealed class ToolsListResult
{
    [JsonPropertyName("tools")]
    public List<ToolInfo> Tools { get; set; } = [];
}

public sealed class ToolInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; set; }
}

/// <summary>
/// resources/list 响应结果
/// </summary>
public sealed class ResourcesListResult
{
    [JsonPropertyName("resources")]
    public List<JsonElement> Resources { get; set; } = [];
}

public sealed class ResourcesReadResult
{
    [JsonPropertyName("contents")]
    public List<JsonElement> Contents { get; set; } = [];
}

public sealed class PromptsListResult
{
    [JsonPropertyName("prompts")]
    public List<JsonElement> Prompts { get; set; } = [];
}

public sealed class PromptsGetResult
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("messages")]
    public List<JsonElement> Messages { get; set; } = [];
}

public sealed class EmptyResult
{
}
