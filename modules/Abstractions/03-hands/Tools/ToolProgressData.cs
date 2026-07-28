namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具进度数据 — 对齐 TS ToolProgressData
/// 所有工具进度类型的基类，通过 ProgressType 区分具体类型
/// </summary>
public sealed class ToolProgressData
{
    /// <summary>
    /// 进度类型标识 — 对齐 TS type 字段
    /// 如 "bash_progress", "mcp_progress", "agent_progress", "web_search_progress" 等
    /// </summary>
    public required string ProgressType { get; init; }

    /// <summary>
    /// 工具调用 ID — 对齐 TS toolUseID
    /// </summary>
    public required string ToolUseId { get; init; }

    /// <summary>
    /// 进度消息文本
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 已用时间（毫秒）— 对齐 TS elapsedTimeMs
    /// </summary>
    public long? ElapsedTimeMs { get; init; }

    /// <summary>
    /// 扩展数据 — 各工具类型的额外进度信息
    /// 如 Bash: output/fullOutput/totalLines/totalBytes
    /// 如 MCP: serverName/toolName/status
    /// </summary>
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// 工具进度回调委托 — 对齐 TS ToolCallProgress
/// </summary>
public delegate void ToolProgressCallback(ToolProgressData progress);
