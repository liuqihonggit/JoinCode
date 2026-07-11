namespace JoinCode.Transport.Bridge;

/// <summary>
/// Bridge 服务器消息 - 简单消息格式用于 BridgeServer
/// </summary>
public sealed class BridgeServerMessage
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Data { get; init; }

    [JsonPropertyName("requestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestId { get; init; }
}

public sealed class BridgeConnectedData
{
    [JsonPropertyName("clientId")]
    public required string ClientId { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }
}

public sealed class BridgeHealthData
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("clients")]
    public required int Clients { get; init; }
}

public sealed class BridgeClientsData
{
    [JsonPropertyName("clients")]
    public required List<string> Clients { get; init; }
}

public sealed class BridgeErrorData
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }
}

public sealed class BridgeFileContentData
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

public sealed class BridgeSelectionSetData
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }
}

public sealed class BridgeCommandExecutedData
{
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("success")]
    public required bool Success { get; init; }
}
