namespace Core.Agents;

/// <summary>
/// AgentStatus 与 TaskExecutionStatus 之间的映射扩展方法
/// </summary>
public static class AgentStatusMappingExtensions
{
    /// <summary>
    /// 将 TaskExecutionStatus 映射为 AgentStatus
    /// </summary>
    public static AgentStatus ToAgentStatus(this TaskExecutionStatus state) => state switch
    {
        TaskExecutionStatus.Pending => AgentStatus.Pending,
        TaskExecutionStatus.Running => AgentStatus.Running,
        TaskExecutionStatus.Paused => AgentStatus.Running,
        TaskExecutionStatus.Completed => AgentStatus.Completed,
        TaskExecutionStatus.Failed => AgentStatus.Failed,
        TaskExecutionStatus.Cancelled => AgentStatus.Stopped,
        _ => AgentStatus.Pending
    };
}
