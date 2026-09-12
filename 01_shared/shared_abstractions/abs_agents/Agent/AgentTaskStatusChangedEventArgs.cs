
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Agent 任务状态变更事件参数
/// </summary>
public sealed class AgentTaskStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// 任务 ID
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// 旧状态
    /// </summary>
    public TaskExecutionStatus OldStatus { get; }

    /// <summary>
    /// 新状态
    /// </summary>
    public TaskExecutionStatus NewStatus { get; }

    /// <summary>
    /// 状态变更时间
    /// </summary>
    public DateTime ChangedAt { get; }

    /// <summary>
    /// 附加消息
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public AgentTaskStatusChangedEventArgs(
        string taskId,
        TaskExecutionStatus oldStatus,
        TaskExecutionStatus newStatus,
        string? message = null)
    {
        TaskId = taskId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        ChangedAt = DateTime.UtcNow;
        Message = message;
    }
}
