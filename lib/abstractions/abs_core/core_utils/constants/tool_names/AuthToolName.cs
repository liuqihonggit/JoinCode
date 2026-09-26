namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 认证工具名称枚举 — 对应 ToolCategory.McpAuth
/// </summary>
public enum AuthToolName {
    /// <summary>
    /// 获取认证状态。
    /// ⚠️ 未实现。未来实现时：归入 ToolCategory.McpAuth；只读操作可标 ConcurrencySafe=true。
    /// </summary>
    [EnumValue("auth_get_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AuthGetStatus,

    /// <summary>
    /// 刷新认证令牌。
    /// ⚠️ 未实现。未来实现时：归入 ToolCategory.McpAuth；涉及凭据写入，禁止标 ConcurrencySafe=true。
    /// </summary>
    [EnumValue("auth_refresh")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AuthRefresh,

    /// <summary>
    /// 登出 — 撤销认证会话。
    /// ⚠️ 未实现。未来实现时：归入 ToolCategory.McpAuth；敏感操作，禁止标 ConcurrencySafe=true。
    /// </summary>
    [EnumValue("auth_logout")]
    [SecurityClass("sensitive", AutoAllowed = false, PlanDenied = true, AskAllowed = true)]
    AuthLogout,
}
