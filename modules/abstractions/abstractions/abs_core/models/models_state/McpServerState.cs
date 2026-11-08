namespace JoinCode.Abstractions.State;

/// <summary>
/// MCP 连接状态 — [EnumValue] 由 EnumMetadataGenerator 自动生成映射
/// </summary>
public enum McpConnectionStatus {
    [EnumValue("disconnected")] Disconnected,
    [EnumValue("connecting")] Connecting,
    [EnumValue("connected")] Connected,
    [EnumValue("error")] Error
}

/// <summary>
/// MCP 服务器状态
/// </summary>
public sealed record McpServerState {
    /// <summary>
    /// 服务器名称
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 服务器 ID
    /// </summary>
    public string ServerId { get; init; } = string.Empty;

    /// <summary>
    /// 连接状态
    /// </summary>
    public McpConnectionStatus Status { get; init; } = McpConnectionStatus.Disconnected;

    /// <summary>
    /// 最后错误信息
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// 连接时间
    /// </summary>
    public DateTime? ConnectedAt { get; init; }
}
