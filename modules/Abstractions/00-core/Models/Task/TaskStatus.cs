namespace JoinCode.Abstractions.Models.Task;

/// <summary>
/// 任务状态枚举
/// </summary>
public enum TaskStatus
{
    [EnumValue("pending")] Pending,
    [EnumValue("waiting_for_dependencies")] WaitingForDependencies,
    [EnumValue("in_progress")] InProgress,
    [EnumValue("paused")] Paused,
    [EnumValue("completed")] Completed,
    [EnumValue("failed")] Failed,
    [EnumValue("cancelled")] Cancelled,
    [EnumValue("stopped")] Stopped
}


