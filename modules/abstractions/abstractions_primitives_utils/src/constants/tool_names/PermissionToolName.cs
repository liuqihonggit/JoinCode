namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 权限工具名称枚举 — 对应 ToolCategory.Permission
/// </summary>
public enum PermissionToolName {
    [EnumValue("permission_add_rule")]
    [SecurityClass("sensitive", AutoAllowed = false, PlanDenied = true, AskAllowed = true)]
    PermissionAddRule,

    [EnumValue("permission_remove_rule")]
    [SecurityClass("sensitive", AutoAllowed = false, PlanDenied = true, AskAllowed = true)]
    PermissionRemoveRule,

    [EnumValue("permission_list_rules")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    PermissionListRules,

    [EnumValue("permission_check_tool")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    PermissionCheckTool,

    [EnumValue("permission_check_path")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    PermissionCheckPath,

    [EnumValue("permission_get_agent_rule")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    PermissionGetAgentRule,

    [EnumValue("permission_clear_rules")]
    [SecurityClass("sensitive", AutoAllowed = false, PlanDenied = true, AskAllowed = true)]
    PermissionClearRules,
}
