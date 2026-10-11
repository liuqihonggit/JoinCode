namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 分析工具名称枚举 — 对应 ToolCategory.Analytics
/// </summary>
public enum AnalyticsToolName {
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
}
