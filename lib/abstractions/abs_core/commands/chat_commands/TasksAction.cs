namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// /tasks 命令的子动作分类集合。
/// 适用范围: /tasks [kill|detail|complete|todo]
/// create/new/update 走 CrudAction 复用,不纳入此枚举。
///
/// 使用示例:
/// - FromValue("kill")  → TasksAction.Kill
/// - FromValue("DETAIL") → TasksAction.Detail (OrdinalIgnoreCase)
/// - TasksAction.Complete.ToValue() → "complete"
/// </summary>
public enum TasksAction
{
    /// <summary>停止/终止指定任务 (kill 旧别名为 stop,但未在命令中启用,保持单一入口)</summary>
    [EnumValue("kill")] Kill,

    /// <summary>查看任务详情</summary>
    [EnumValue("detail")] Detail,

    /// <summary>完成任务(状态 → Completed)</summary>
    [EnumValue("complete")] Complete,

    /// <summary>列出待办事项 (TodoService 列表视图)</summary>
    [EnumValue("todo")] Todo,
}
