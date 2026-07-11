namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Todo 工具名称枚举
/// </summary>
public enum TodoToolName
{
    [EnumValue("todo_create")] TodoCreate,
    [EnumValue("todo_list")] TodoList,
    [EnumValue("todo_update")] TodoUpdate,
    [EnumValue("todo_delete")] TodoDelete,
    [EnumValue("TodoWrite")] TodoWrite,
    [EnumValue("todo_read")] TodoRead,
}
