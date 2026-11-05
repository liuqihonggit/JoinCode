namespace JoinCode.Abstractions.Mcp.Protocol;

public abstract record JsonRpcMessage {
    /// <summary>获取或设置 JSON-RPC 协议版本。</summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";
}

public sealed record JsonRpcRequest : JsonRpcMessage {
    /// <summary>获取或设置请求标识。</summary>
    [JsonPropertyName("id")]
    public JsonRpcId Id { get; init; }

    /// <summary>获取或设置方法名称。</summary>
    [JsonPropertyName("method")]
    public string Method { get; init; } = string.Empty;

    /// <summary>获取或设置参数。</summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; init; }
}

public sealed record JsonRpcResponse : JsonRpcMessage {
    /// <summary>获取或设置响应标识。</summary>
    [JsonPropertyName("id")]
    public JsonRpcId Id { get; init; }

    /// <summary>获取或设置结果。</summary>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; init; }

    /// <summary>获取或设置错误对象。</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; init; }
}

public sealed record JsonRpcError {
    /// <summary>获取或设置错误码。</summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>获取或设置错误消息。</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>获取或设置附加数据。</summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }
}

public sealed record JsonRpcNotification : JsonRpcMessage {
    /// <summary>获取或设置方法名称。</summary>
    [JsonPropertyName("method")]
    public string Method { get; init; } = string.Empty;

    /// <summary>获取或设置参数。</summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; init; }
}