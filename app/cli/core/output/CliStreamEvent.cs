namespace JoinCode.Cli.Output;

/// <summary>
/// NDJSON 事件 — AX 模式下每行一个 JSON 对象的结构化事件流
/// </summary>
public sealed class CliStreamEvent {
    /// <summary>事件类型（text/thinking/tool_start/tool_end/tool_progress/loop_detected/timing/done）</summary>
    public string Type { get; init; }

    /// <summary>UTC 时间戳（ISO 8601 格式）</summary>
    public string Timestamp { get; init; }

    /// <summary>事件负载</summary>
    public CliStreamEventData? Data { get; init; }

    /// <summary>初始化 <see cref="CliStreamEvent"/> 实例，并自动生成 UTC 时间戳</summary>
    /// <param name="type">事件类型（text/thinking/tool_start/tool_end/tool_progress/loop_detected/timing/done）</param>
    public CliStreamEvent(string type) {
        Type = type;
        Timestamp = DateTime.UtcNow.ToString("O");
    }
}

/// <summary>
/// NDJSON 事件负载 — 使用强类型属性替代 Dictionary&lt;string, object&gt;（AOT 兼容）
/// </summary>
public sealed class CliStreamEventData {
    /// <summary>文本内容（text 事件）</summary>
    public string? Content { get; init; }

    /// <summary>工具名称（tool_* 事件）</summary>
    public string? ToolName { get; init; }

    /// <summary>工具调用标识（tool_* 事件）</summary>
    public string? ToolCallId { get; init; }

    /// <summary>工具调用参数 JSON（tool_start 事件）</summary>
    public string? Arguments { get; init; }

    /// <summary>是否为错误（tool_end 事件）</summary>
    public bool? IsError { get; init; }

    /// <summary>结果长度（tool_end 事件）</summary>
    public int? ResultLength { get; init; }

    /// <summary>进度类型（tool_progress 事件）</summary>
    public string? ProgressType { get; init; }

    /// <summary>进度消息（tool_progress 事件）</summary>
    public string? ProgressMessage { get; init; }

    /// <summary>循环触发次数（loop_detected 事件）</summary>
    public int? TriggerCount { get; init; }

    /// <summary>循环起始索引（loop_detected 事件）</summary>
    public int? LoopStartIndex { get; init; }

    /// <summary>摘要（done/timing 事件）</summary>
    public string? Summary { get; init; }

    /// <summary>令牌用量（done/timing 事件）</summary>
    public TokenUsage? Usage { get; init; }

    /// <summary>模型标识（done 事件）</summary>
    public string? ModelId { get; init; }
}