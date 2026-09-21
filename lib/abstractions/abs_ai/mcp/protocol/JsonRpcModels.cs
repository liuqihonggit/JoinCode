namespace JoinCode.Abstractions.Mcp.Protocol;

public abstract class JsonRpcMessage {
    /// <summary>获取或设置 JSON-RPC 协议版本。</summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
}

public class JsonRpcRequest : JsonRpcMessage {
    /// <summary>获取或设置请求标识。</summary>
    [JsonPropertyName("id")]
    public JsonRpcId Id { get; set; }

    /// <summary>获取或设置方法名称。</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>获取或设置参数。</summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

public class JsonRpcResponse : JsonRpcMessage {
    /// <summary>获取或设置响应标识。</summary>
    [JsonPropertyName("id")]
    public JsonRpcId Id { get; set; }

    /// <summary>获取或设置结果。</summary>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; set; }

    /// <summary>获取或设置错误对象。</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }
}

public class JsonRpcError {
    /// <summary>获取或设置错误码。</summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>获取或设置错误消息。</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>获取或设置附加数据。</summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}

public class JsonRpcNotification : JsonRpcMessage {
    /// <summary>获取或设置方法名称。</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>获取或设置参数。</summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}