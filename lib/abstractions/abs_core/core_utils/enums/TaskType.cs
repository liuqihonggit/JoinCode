namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 任务类型 - 用于区分不同类型的后台任务
/// </summary>
public enum TaskType
{
    [EnumValue("local_bash")] LocalBash,
    [EnumValue("local_agent")] LocalAgent,
    [EnumValue("remote_agent")] RemoteAgent,
    [EnumValue("in_process_teammate")] InProcessTeammate,
    [EnumValue("local_workflow")] LocalWorkflow,
    [EnumValue("monitor_mcp")] MonitorMcp,
    [EnumValue("dream")] Dream
}

/// <summary>
/// 任务状态 - 与TaskState保持一致但用于做梦系统
/// </summary>
public enum DreamTaskStatus
{
    [EnumValue("pending")] Pending,
    [EnumValue("running")] Running,
    [EnumValue("completed")] Completed,
    [EnumValue("failed")] Failed,
    [EnumValue("killed")] Killed
}

/// <summary>
/// 做梦阶段
/// </summary>
public enum DreamPhase
{
    [EnumValue("starting")] Starting,
    [EnumValue("updating")] Updating
}

/// <summary>
/// 任务ID前缀映射
/// </summary>
public static class TaskIdPrefixes
{
    private static readonly Dictionary<TaskType, char> Prefixes = new()
    {
        [TaskType.LocalBash] = 'b',
        [TaskType.LocalAgent] = 'a',
        [TaskType.RemoteAgent] = 'r',
        [TaskType.InProcessTeammate] = 't',
        [TaskType.LocalWorkflow] = 'w',
        [TaskType.MonitorMcp] = 'm',
        [TaskType.Dream] = 'd'
    };

    public static char GetPrefix(TaskType type) =>
        Prefixes.TryGetValue(type, out var prefix) ? prefix : 'x';
}
