namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 任务管理工具名称枚举
/// </summary>
public enum TaskToolName
{
    [EnumValue("TaskCreate")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TaskCreate,

    [EnumValue("TaskList")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TaskList,

    [EnumValue("TaskUpdate")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TaskUpdate,

    [EnumValue("task_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TaskDelete,

    [EnumValue("TaskStop")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TaskStop,

    [EnumValue("TaskGet")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TaskGet,

    [EnumValue("task_set_dependency")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TaskSetDependency,

    [EnumValue("task_remove_dependency")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TaskRemoveDependency,

    [EnumValue("task_get_dependencies")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TaskGetDependencies,

    [EnumValue("task_can_execute")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TaskCanExecute,

    [EnumValue("task_stop_batch")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TaskStopBatch,

    [EnumValue("task_list_running")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TaskListRunning,
}
