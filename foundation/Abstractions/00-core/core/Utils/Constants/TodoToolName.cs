namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Todo 工具名称枚举
/// </summary>
public enum TodoToolName
{
    [EnumValue("todo_create")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TodoCreate,

    [EnumValue("todo_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TodoList,

    [EnumValue("todo_update")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TodoUpdate,

    [EnumValue("todo_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    TodoDelete,

    [EnumValue("TodoWrite")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TodoWrite,

    [EnumValue("todo_read")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    TodoRead,
}
