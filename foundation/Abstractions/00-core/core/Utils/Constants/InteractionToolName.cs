namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 用户交互/权限/认证/配置/分析工具名称枚举
/// </summary>
public enum InteractionToolName
{
    [EnumValue("ask_user")] AskUser,
    [EnumValue("confirm_action")] ConfirmAction,
    [EnumValue("AskUserQuestion")] AskUserQuestion,
    [EnumValue("auth_get_status")] AuthGetStatus,
    [EnumValue("auth_refresh")] AuthRefresh,
    [EnumValue("auth_logout")] AuthLogout,
    [EnumValue("Config")] Config,
    [EnumValue("config_get")] ConfigGet,
    [EnumValue("config_set")] ConfigSet,
    [EnumValue("config_list")] ConfigList,
    [EnumValue("permission_add_rule")] PermissionAddRule,
    [EnumValue("permission_remove_rule")] PermissionRemoveRule,
    [EnumValue("permission_list_rules")] PermissionListRules,
    [EnumValue("permission_check_tool")] PermissionCheckTool,
    [EnumValue("permission_check_path")] PermissionCheckPath,
    [EnumValue("permission_get_agent_rule")] PermissionGetAgentRule,
    [EnumValue("permission_clear_rules")] PermissionClearRules,
    [EnumValue("analytics_report")] AnalyticsReport,
    [EnumValue("analytics_tools")] AnalyticsTools,
    [EnumValue("analytics_events")] AnalyticsEvents,
    [EnumValue("analytics_export")] AnalyticsExport,
    [EnumValue("analytics_clear")] AnalyticsClear,
    [EnumValue("policy_check")] PolicyCheck,
    [EnumValue("policy_list")] PolicyList,
}
