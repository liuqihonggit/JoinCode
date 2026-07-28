namespace JoinCode.Abstractions.Models.Shell;

public sealed record ShellBackgroundTaskInfo
{
    public required string TaskId { get; init; }
    public required string Command { get; init; }
    public required TaskExecutionStatus Status { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Stdout { get; init; }
    public string? Stderr { get; init; }
    public int? ExitCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? AgentId { get; init; }
}
