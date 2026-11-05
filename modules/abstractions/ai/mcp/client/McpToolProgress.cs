namespace JoinCode.Abstractions.Mcp.Client;

public sealed class McpToolProgress {
    /// <summary>获取进度类型。</summary>
    public required string Type { get; init; }

    /// <summary>获取进度状态。</summary>
    public required string Status { get; init; }

    /// <summary>获取服务器名称。</summary>
    public string? ServerName { get; init; }

    /// <summary>获取工具名称。</summary>
    public string? ToolName { get; init; }

    /// <summary>获取当前进度值。</summary>
    public double? Progress { get; init; }

    /// <summary>获取总量。</summary>
    public double? Total { get; init; }

    /// <summary>获取进度消息。</summary>
    public string? ProgressMessage { get; init; }

    /// <summary>获取已耗时（毫秒）。</summary>
    public long? ElapsedTimeMs { get; init; }
}

public delegate void McpProgressCallback(McpToolProgress progress);

public enum McpProgressStatus {
    [EnumValue("started")] Started,
    [EnumValue("progress")] Progress,
    [EnumValue("completed")] Completed,
    [EnumValue("failed")] Failed,
}