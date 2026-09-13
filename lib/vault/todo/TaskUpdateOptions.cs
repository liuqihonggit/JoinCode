namespace Services.Todo.ToolHandlers;

/// <summary>
/// 任务更新选项 — 封装 TaskUpdateAsync 的 MCP 工具参数
/// </summary>
public sealed record TaskUpdateOptions
{
    /// <summary>任务 ID。</summary>
    [McpToolParameter("Task ID")]
    public required string TaskId { get; init; }

    /// <summary>新标题(可选)。</summary>
    [McpToolParameter("New title (optional)", Required = false)]
    public string? Title { get; init; }

    /// <summary>新描述(可选)。</summary>
    [McpToolParameter("New description (optional)", Required = false)]
    public string? Description { get; init; }

    /// <summary>新状态(可选)。</summary>
    [McpToolParameter("New status (optional)", Required = false)]
    public string? Status { get; init; }

    /// <summary>新指派人(可选)。</summary>
    [McpToolParameter("New assignee (optional)", Required = false)]
    public string? Assignee { get; init; }

    /// <summary>新截止日期(可选)。</summary>
    [McpToolParameter("New due date (optional)", Required = false)]
    public DateTime? DueDate { get; init; }

    /// <summary>新优先级(可选)。</summary>
    [McpToolParameter("New priority (optional)", Required = false)]
    public string? Priority { get; init; }

    /// <summary>新标签列表(可选)。</summary>
    [McpToolParameter("New tag list (optional)", Required = false)]
    public List<string>? Tags { get; init; }
}
