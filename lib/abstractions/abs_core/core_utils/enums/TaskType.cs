namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 任务类型 - 用于区分不同类型的后台任务
/// </summary>
public enum TaskType
{
    LocalBash,
    LocalAgent,
    RemoteAgent,
    InProcessTeammate,
    LocalWorkflow,
    MonitorMcp,
    Dream
}

/// <summary>
/// 任务状态 - 与TaskState保持一致但用于做梦系统
/// </summary>
public enum DreamTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Killed
}

/// <summary>
/// 做梦阶段
/// </summary>
public enum DreamPhase
{
    Starting,
    Updating
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
