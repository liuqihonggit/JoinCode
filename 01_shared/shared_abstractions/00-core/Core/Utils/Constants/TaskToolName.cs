namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 任务管理工具名称枚举
/// </summary>
public enum TaskToolName
{
    [EnumValue("task_create")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TaskCreate,

    [EnumValue("task_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TaskList,

    [EnumValue("task_update")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TaskUpdate,

    [EnumValue("task_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TaskDelete,

    [EnumValue("task_stop")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TaskStop,

    [EnumValue("task_get")]
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
