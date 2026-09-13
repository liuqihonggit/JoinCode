namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// 聊天命令子操作:CRUD 类(增删改查)
/// 适用于具有 list/create/read/update/delete 五态的命令(如 /session, /branch, /worktree, /config, /permissions, /tasks)
/// 别名 Ls/New/Rm 兼容 Unix 习惯(ls/new/rm 等价于 list/create/delete)
///
/// 使用示例:
/// - FromValue("list") → CrudAction.List
/// - FromValue("ls")   → CrudAction.Ls (OrdinalIgnoreCase 别名)
/// - CrudAction.Delete.ToValue() → "delete"
/// - CrudAction.Rm.ToValue()     → "rm"
/// </summary>
public enum CrudAction
{
    /// <summary>列表 — 查看所有项</summary>
    [EnumValue("list")] List,

    /// <summary>列表别名 — 等价于 list</summary>
    [EnumValue("ls")] Ls,

    /// <summary>创建 — 新建一个项</summary>
    [EnumValue("create")] Create,

    /// <summary>创建别名 — 等价于 create</summary>
    [EnumValue("new")] New,

    /// <summary>读取 — 查看单个项详情</summary>
    [EnumValue("read")] Read,

    /// <summary>更新 — 修改现有项</summary>
    [EnumValue("update")] Update,

    /// <summary>删除 — 移除一个项</summary>
    [EnumValue("delete")] Delete,

    /// <summary>删除别名 — 等价于 delete</summary>
    [EnumValue("rm")] Rm,

    /// <summary>删除别名 — 等价于 delete (ConfigCommand 兼容)</summary>
    [EnumValue("remove")] Remove,
}
