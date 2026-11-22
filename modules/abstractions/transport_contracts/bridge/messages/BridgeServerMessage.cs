namespace JoinCode.Transport.Bridge;

/// <summary>
/// Bridge 服务器消息 - 简单消息格式用于 BridgeServer
/// </summary>
public sealed class BridgeServerMessage {
    /// <summary>消息类型字符串</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>消息数据载荷</summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Data { get; init; }

    /// <summary>对应请求的 ID</summary>
    [JsonPropertyName("requestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestId { get; init; }
}

/// <summary>
/// Bridge 连接成功数据
/// </summary>
public sealed class BridgeConnectedData {
    /// <summary>客户端 ID</summary>
    [JsonPropertyName("clientId")]
    public required string ClientId { get; init; }

    /// <summary>协议版本</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }
}

/// <summary>
/// Bridge 健康状态数据
/// </summary>
public sealed class BridgeHealthData {
    /// <summary>健康状态文本</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>当前连接的客户端数</summary>
    [JsonPropertyName("clients")]
    public required int Clients { get; init; }
}

/// <summary>
/// Bridge 客户端列表数据
/// </summary>
public sealed class BridgeClientsData {
    /// <summary>客户端 ID 列表</summary>
    [JsonPropertyName("clients")]
    public required List<string> Clients { get; init; }
}

/// <summary>
/// Bridge 错误数据
/// </summary>
public sealed class BridgeErrorData {
    /// <summary>错误描述</summary>
    [JsonPropertyName("error")]
    public required string Error { get; init; }
}

/// <summary>
/// Bridge 文件内容数据
/// </summary>
public sealed class BridgeFileContentData {
    /// <summary>文件路径</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>文件内容</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>错误描述</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>
/// Bridge 选择集数据
/// </summary>
public sealed class BridgeSelectionSetData {
    /// <summary>是否成功</summary>
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    /// <summary>错误描述</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}

/// <summary>
/// Bridge 命令执行结果数据
/// </summary>
public sealed class BridgeCommandExecutedData {
    /// <summary>执行的命令</summary>
    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; init; }

    /// <summary>是否成功</summary>
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    /// <summary>命令标准输出</summary>
    [JsonPropertyName("output")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Output { get; init; }

    /// <summary>错误描述</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    /// <summary>进程退出码</summary>
    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; init; }

    /// <summary>执行耗时（毫秒）</summary>
    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; init; }
}