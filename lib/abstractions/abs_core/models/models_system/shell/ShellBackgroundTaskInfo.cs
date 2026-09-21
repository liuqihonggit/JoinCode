namespace JoinCode.Abstractions.Models.Shell;

public sealed record ShellBackgroundTaskInfo {
    /// <summary>获取任务标识。</summary>
    public required string TaskId { get; init; }
    /// <summary>获取命令。</summary>
    public required string Command { get; init; }
    /// <summary>获取任务执行状态。</summary>
    public required TaskExecutionStatus Status { get; init; }
    /// <summary>获取创建时间。</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>获取开始时间。</summary>
    public DateTime? StartedAt { get; init; }
    /// <summary>获取完成时间。</summary>
    public DateTime? CompletedAt { get; init; }
    /// <summary>获取标准输出。</summary>
    public string? Stdout { get; init; }
    /// <summary>获取标准错误。</summary>
    public string? Stderr { get; init; }
    /// <summary>获取退出码。</summary>
    public int? ExitCode { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>获取工作目录。</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>获取代理标识。</summary>
    public string? AgentId { get; init; }
}