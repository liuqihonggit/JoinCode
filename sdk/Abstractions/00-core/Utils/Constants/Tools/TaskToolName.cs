namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 任务管理工具名称枚举
/// </summary>
public enum TaskToolName
{
    [EnumValue("TaskCreate")] TaskCreate,
    [EnumValue("TaskList")] TaskList,
    [EnumValue("TaskUpdate")] TaskUpdate,
    [EnumValue("task_delete")] TaskDelete,
    [EnumValue("TaskStop")] TaskStop,
    [EnumValue("TaskGet")] TaskGet,
    [EnumValue("task_set_dependency")] TaskSetDependency,
    [EnumValue("task_remove_dependency")] TaskRemoveDependency,
    [EnumValue("task_get_dependencies")] TaskGetDependencies,
    [EnumValue("task_can_execute")] TaskCanExecute,
    [EnumValue("task_stop_batch")] TaskStopBatch,
    [EnumValue("task_list_running")] TaskListRunning,
}
