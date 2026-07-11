namespace JoinCode.Abstractions.Security;

/// <summary>
/// 权限模式枚举 — 统一全局权限模式/TUI展示模式/Agent权限模式
/// 合并自: ToolPermissionMode (AcceptEdits, DontAsk), AgentPermissionMode (Deny)
/// </summary>
public enum PermissionMode
{
    /// <summary>
    /// 默认模式：根据配置自动判断是否需要确认
    /// </summary>
    [EnumValue("default")] Default,

    /// <summary>
    /// 计划模式：读取操作自动批准，写入操作需要确认
    /// </summary>
    [EnumValue("plan")] Plan,

    /// <summary>
    /// 绕过权限检查：所有操作自动批准（谨慎使用）
    /// </summary>
    [EnumValue("bypassPermissions")] BypassPermissions,

    /// <summary>
    /// 自动模式：根据工具类型和参数自动判断
    /// </summary>
    [EnumValue("auto")] Auto,

    /// <summary>
    /// 询问模式：每个操作都需要用户确认
    /// </summary>
    [EnumValue("ask")] Ask,

    /// <summary>
    /// 接受编辑模式：自动接受文件编辑操作（来自 ToolPermissionMode）
    /// </summary>
    [EnumValue("acceptEdits")] AcceptEdits,

    /// <summary>
    /// 不再询问模式：自动批准且不再提示（来自 ToolPermissionMode）
    /// </summary>
    [EnumValue("dontAsk")] DontAsk,

    /// <summary>
    /// 拒绝模式：拒绝所有操作（来自 AgentPermissionMode）
    /// </summary>
    [EnumValue("deny")] Deny
}
