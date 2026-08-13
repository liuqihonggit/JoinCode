namespace JoinCode.Cli.Output;

/// <summary>
/// NDJSON 事件 — AX 模式下每行一个 JSON 对象的结构化事件流
/// </summary>
public sealed class CliStreamEvent
{
    /// <summary>事件类型（text/thinking/tool_start/tool_end/tool_progress/loop_detected/timing/done）</summary>
    public string Type { get; init; }

    /// <summary>UTC 时间戳（ISO 8601 格式）</summary>
    public string Timestamp { get; init; }

    /// <summary>事件负载</summary>
    public CliStreamEventData? Data { get; init; }

    public CliStreamEvent(string type)
    {
        Type = type;
        Timestamp = DateTime.UtcNow.ToString("O");
    }
}

/// <summary>
/// NDJSON 事件负载 — 使用强类型属性替代 Dictionary&lt;string, object&gt;（AOT 兼容）
/// </summary>
public sealed class CliStreamEventData
{
    public string? Content { get; init; }
    public string? ToolName { get; init; }
    public string? ToolCallId { get; init; }
    public string? Arguments { get; init; }
    public bool? IsError { get; init; }
    public int? ResultLength { get; init; }
    public string? ProgressType { get; init; }
    public string? ProgressMessage { get; init; }
    public int? TriggerCount { get; init; }
    public int? LoopStartIndex { get; init; }
    public string? Summary { get; init; }
    public TokenUsage? Usage { get; init; }
    public string? ModelId { get; init; }
}
