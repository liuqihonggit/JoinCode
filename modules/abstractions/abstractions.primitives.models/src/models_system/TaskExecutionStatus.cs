namespace JoinCode.Abstractions.State;

/// <summary>
/// 任务执行状态
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 TaskExecutionStatusEnumConstants + TaskExecutionStatusExtensions
/// 合并自: WorkflowState, AgentExecutionStatus, ShellBackgroundTaskStatus, AgentTaskStatus
/// </summary>
public enum TaskExecutionStatus {
    /// <summary>等待执行</summary>
    [EnumValue("pending")] Pending = 0,

    /// <summary>执行中</summary>
    [EnumValue("running")] Running = 1,

    /// <summary>暂停</summary>
    [EnumValue("paused")] Paused = 2,

    /// <summary>已完成</summary>
    [EnumValue("completed")] Completed = 3,

    /// <summary>已失败</summary>
    [EnumValue("failed")] Failed = 4,

    /// <summary>已取消</summary>
    [EnumValue("cancelled")] Cancelled = 5,

    /// <summary>等待依赖</summary>
    [EnumValue("waitingForDependency")] WaitingForDependency = 6,

    /// <summary>就绪（依赖已满足，等待调度）</summary>
    [EnumValue("ready")] Ready = 7,

    /// <summary>已停止（手动停止，非正常完成）</summary>
    [EnumValue("stopped")] Stopped = 8
}

/// <summary>
/// 任务执行状态扩展方法 — 提取自 AgentStateMachine/AgentCoordinator/TaskRuntime 等多处重复的终态/活跃态判断
/// </summary>
public static class TaskExecutionStatusHelper {
    /// <summary>
    /// 是否处于终态（Completed/Failed/Cancelled/Stopped）— 不可再转换（除重试外）
    /// </summary>
    public static bool IsTerminal(this TaskExecutionStatus status)
        => status is TaskExecutionStatus.Completed or TaskExecutionStatus.Failed or TaskExecutionStatus.Cancelled or TaskExecutionStatus.Stopped;

    /// <summary>
    /// 是否处于活跃态（Running/Ready/WaitingForDependency）— 正在执行或即将执行
    /// </summary>
    public static bool IsActive(this TaskExecutionStatus status)
        => status is TaskExecutionStatus.Running or TaskExecutionStatus.Ready or TaskExecutionStatus.WaitingForDependency;
}

public static class TaskExecutionStatusSymbolExtensions {
    /// <summary>将任务执行状态转换为状态符号。</summary>
    /// <param name="status">任务执行状态。</param>
    public static StatusSymbol ToStatusSymbol(this TaskExecutionStatus status)
        => status switch {
            TaskExecutionStatus.Running => StatusSymbol.Refresh,
            TaskExecutionStatus.Completed => StatusSymbol.Tick,
            TaskExecutionStatus.Failed => StatusSymbol.Cross,
            TaskExecutionStatus.Cancelled => StatusSymbol.Prohibited,
            _ => StatusSymbol.Circle
        };
}
