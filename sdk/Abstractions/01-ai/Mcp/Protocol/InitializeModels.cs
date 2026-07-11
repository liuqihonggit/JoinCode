namespace JoinCode.Abstractions.Mcp.Protocol;

public class InitializeRequestParams
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";

    [JsonPropertyName("capabilities")]
    public ClientCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("clientInfo")]
    public Implementation ClientInfo { get; set; } = new();
}

public class InitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";

    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public Implementation ServerInfo { get; set; } = new();

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }
}

public class ClientCapabilities
{
    [JsonPropertyName("sampling")]
    public JsonElement? Sampling { get; set; }

    [JsonPropertyName("roots")]
    public JsonElement? Roots { get; set; }

    [JsonPropertyName("elicitation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Elicitation { get; set; }
}

public class ServerCapabilities
{
    [JsonPropertyName("tools")]
    public ToolsCapability? Tools { get; set; }

    [JsonPropertyName("resources")]
    public ResourcesCapability? Resources { get; set; }

    [JsonPropertyName("prompts")]
    public PromptsCapability? Prompts { get; set; }

    [JsonPropertyName("logging")]
    public LoggingCapability? Logging { get; set; }
}

public class ToolsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

public class ResourcesCapability
{
    [JsonPropertyName("subscribe")]
    public bool Subscribe { get; set; }

    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

public class PromptsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

public class LoggingCapability
{
    [JsonPropertyName("level")]
    public string? Level { get; set; }
}

public class Implementation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}
