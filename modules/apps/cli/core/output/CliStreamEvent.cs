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
    [JsonPropertyName("model_id")]
    public string? ModelId { get; init; }

    /// <summary>停止原因（done 事件）— stop/tool_calls/length/content_filter 等</summary>
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }

    /// <summary>退出码（result 事件）— Bug 14: NDJSON 最终 result 事件携带 exitCode</summary>
    public int? ExitCode { get; set; }

    /// <summary>结构化错误（result 事件，exitCode!=0 时非 null）— Bug 14: NDJSON 错误事件</summary>
    public CliStructuredError? Error { get; set; }

    /// <summary>子代理标识（agent_started/agent_finished 事件）</summary>
    public string? SubagentId { get; init; }

    /// <summary>代理类型（agent_started 事件）</summary>
    public string? AgentType { get; init; }

    /// <summary>任务描述（agent_started 事件）</summary>
    public string? Description { get; init; }

    /// <summary>代理角色（agent_started 事件）— executor/researcher/planner 等</summary>
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    /// <summary>是否错误（agent_finished 事件）</summary>
    public bool? SubagentIsError { get; init; }

    /// <summary>子代理总 token 消耗（agent_finished 事件）</summary>
    public int? TokenTotal { get; init; }

    /// <summary>当前上下文窗口占用 token 数（context_usage 事件）</summary>
    public int? ContextPromptTokens { get; init; }

    /// <summary>上下文窗口总容量 token 数（context_usage 事件）</summary>
    public int? ContextWindowTokens { get; init; }

    /// <summary>上下文窗口剩余百分比（context_usage 事件）</summary>
    public int? PercentLeft { get; init; }
}