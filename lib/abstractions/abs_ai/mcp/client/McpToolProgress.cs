namespace JoinCode.Abstractions.Mcp.Client;

public sealed class McpToolProgress
{
    public required string Type { get; init; }

    public required string Status { get; init; }

    public string? ServerName { get; init; }

    public string? ToolName { get; init; }

    public double? Progress { get; init; }

    public double? Total { get; init; }

    public string? ProgressMessage { get; init; }

    public long? ElapsedTimeMs { get; init; }
}

public delegate void McpProgressCallback(McpToolProgress progress);

public enum McpProgressStatus
{
    [EnumValue("started")] Started,
    [EnumValue("progress")] Progress,
    [EnumValue("completed")] Completed,
    [EnumValue("failed")] Failed,
}
