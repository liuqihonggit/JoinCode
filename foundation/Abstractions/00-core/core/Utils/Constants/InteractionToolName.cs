namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 用户交互/权限/认证/配置/分析工具名称枚举
/// </summary>
public enum InteractionToolName
{
    [EnumValue("ask_user")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AskUser,

    [EnumValue("confirm_action")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ConfirmAction,

    [EnumValue("AskUserQuestion")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AskUserQuestion,

    [EnumValue("auth_get_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AuthGetStatus,

    [EnumValue("auth_refresh")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AuthRefresh,

    [EnumValue("auth_logout")]
    [SecurityClass("sensitive", AutoAllowed = false, PlanDenied = true, AskAllowed = true)]
    AuthLogout,

    [EnumValue("Config")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    Config,

    [EnumValue("config_get")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    ConfigGet,

    [EnumValue("config_set")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ConfigSet,

    [EnumValue("config_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    ConfigList,

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

    [EnumValue("analytics_report")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AnalyticsReport,

    [EnumValue("analytics_tools")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AnalyticsTools,

    [EnumValue("analytics_events")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AnalyticsEvents,

    [EnumValue("analytics_export")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AnalyticsExport,

    [EnumValue("analytics_clear")]
    [SecurityClass("sensitive", AutoAllowed = false, PlanDenied = true, AskAllowed = true)]
    AnalyticsClear,

    [EnumValue("policy_check")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    PolicyCheck,

    [EnumValue("policy_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    PolicyList,
}
