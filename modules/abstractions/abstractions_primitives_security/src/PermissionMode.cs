namespace JoinCode.Abstractions.Security;

/// <summary>
/// 权限模式枚举 — 5个核心模式
/// 旧值已废弃: Default→Auto, AcceptEdits→Auto, DontAsk→Bypass, BypassPermissions→Bypass, Deny→Ask
/// </summary>
public enum PermissionMode {
    /// <summary>
    /// 计划模式：读取操作自动批准，写入操作需要确认
    /// </summary>
    [EnumValue("plan")] Plan,

    /// <summary>
    /// 标准模式：根据工具类型自动判断，危险操作需确认
    /// </summary>
    [EnumValue("auto")] Auto,

    /// <summary>
    /// 询问模式：每个操作都需要用户确认
    /// </summary>
    [EnumValue("ask")] Ask,

    /// <summary>
    /// 全放行模式：所有操作自动批准（需环境变量或--permission-mode启用）
    /// </summary>
    [EnumValue("bypass")] Bypass,

    /// <summary>
    /// 无人值守/自动化模式：硬规则内全部自动执行 + AI 灰区判定；后台/Goal 使用此模式。
    /// <para>别名 <c>autonomous</c> 作为用户显式选择“完全访问 + AI 监控”的入口值。</para>
    /// </summary>
    [EnumValue("unattended")]
    [EnumValue("autonomous")]
    Unattended
}