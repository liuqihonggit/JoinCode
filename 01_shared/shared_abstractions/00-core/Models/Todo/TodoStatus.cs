namespace JoinCode.Abstractions.Models.Todo;

/// <summary>
/// Todo 状态枚举
/// </summary>
public enum TodoStatus
{
    [EnumValue("pending")] Pending,
    [EnumValue("in_progress")] InProgress,
    [EnumValue("completed")] Completed,
    [EnumValue("cancelled")] Cancelled
}
