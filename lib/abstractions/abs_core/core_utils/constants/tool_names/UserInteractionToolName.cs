namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 用户交互工具名称枚举 — 真正需要等待用户输入的工具
/// 对应 ToolCategory.Interaction。此类工具禁止标记 ConcurrencySafe=true，
/// 否则会绕过 StreamingToolExecutor 的独占调度导致并行卡死。
/// </summary>
public enum UserInteractionToolName {
    /// <summary>
    /// 确认动作 — 向用户请求是/否确认。
    /// ⚠️ 未实现。未来实现时：必须归入 ToolCategory.Interaction，且禁止标 [McpTool(ConcurrencySafe=true)]。
    /// </summary>
    [EnumValue("confirm_action")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ConfirmAction,

    /// <summary>
    /// 向用户提出多选问题以收集信息、澄清歧义或做出决策
    /// </summary>
    [EnumValue("ask_user_question")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AskUserQuestion,
}
