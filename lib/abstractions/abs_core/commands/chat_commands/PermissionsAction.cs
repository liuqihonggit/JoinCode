namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// /permissions 命令的子动作分类集合。
/// 适用范围: /permissions [add|clear|workspace|dirs|directories|show]
/// list/ls/remove/delete/rm 走 CrudAction 复用,不纳入此枚举。
/// add 在此枚举中是因为 Permissions 的 add 语义(添加权限规则)与 CrudAction.Create(创建资源)不同。
///
/// 使用示例:
/// - FromValue("add")        → PermissionsAction.Add
/// - FromValue("WORKSPACE")  → PermissionsAction.Workspace (OrdinalIgnoreCase)
/// - PermissionsAction.Clear.ToValue() → "clear"
/// </summary>
public enum PermissionsAction
{
    /// <summary>添加权限规则 (外层 add,非 CrudAction.Create)</summary>
    [EnumValue("add")] Add,

    /// <summary>清除所有权限规则 (外层 + workspace 子命令共用)</summary>
    [EnumValue("clear")] Clear,

    /// <summary>管理工作区目录 (外层主入口)</summary>
    [EnumValue("workspace")] Workspace,

    /// <summary>工作区目录别名 (workspace 的简写)</summary>
    [EnumValue("dirs")] Dirs,

    /// <summary>工作区目录别名 (workspace 的全称)</summary>
    [EnumValue("directories")] Directories,

    /// <summary>显示工作区详情 (workspace 子命令内 show)</summary>
    [EnumValue("show")] Show,
}
