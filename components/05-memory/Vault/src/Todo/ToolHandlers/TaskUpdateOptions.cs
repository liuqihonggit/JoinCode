namespace Services.Todo.ToolHandlers;

/// <summary>
/// 任务更新选项 — 封装 TaskUpdateAsync 的 MCP 工具参数
/// </summary>
public sealed record TaskUpdateOptions
{
    [McpToolParameter("Task ID")]
    public required string TaskId { get; init; }

    [McpToolParameter("New title (optional)", Required = false)]
    public string? Title { get; init; }

    [McpToolParameter("New description (optional)", Required = false)]
    public string? Description { get; init; }

    [McpToolParameter("New status (optional)", Required = false)]
    public string? Status { get; init; }

    [McpToolParameter("New assignee (optional)", Required = false)]
    public string? Assignee { get; init; }

    [McpToolParameter("New due date (optional)", Required = false)]
    public DateTime? DueDate { get; init; }

    [McpToolParameter("New priority (optional)", Required = false)]
    public string? Priority { get; init; }

    [McpToolParameter("New tag list (optional)", Required = false)]
    public List<string>? Tags { get; init; }
}
