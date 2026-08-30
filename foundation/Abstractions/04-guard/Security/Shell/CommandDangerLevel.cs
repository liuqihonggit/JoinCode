namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 命令危险等级枚举 — 统一的命令危险分级，作为权限决策的唯一依据
/// </summary>
/// <remarks>
/// 分级语义:
/// <list type="bullet">
/// <item><term>Safe</term><description>安全操作 — 自动批准（如 ls/cat/grep/git status）</description></item>
/// <item><term>Dangerous</term><description>危险操作 — 需用户确认（如 rm/del/mv/chmod）</description></item>
/// <item><term>Critical</term><description>极危险操作 — 需用户显式确认，不可批量批准（如 rm -rf/git reset --hard/shutdown）</description></item>
/// <item><term>Forbidden</term><description>绝对禁止 — AI 永远拒绝执行，引导用户在终端手动执行（如 rm -rf //format c:/dd of=/dev/sda/mkfs/fdisk）</description></item>
/// </list>
/// </remarks>
public enum CommandDangerLevel
{
    /// <summary>
    /// 安全操作 — 自动批准，无需确认
    /// </summary>
    [EnumValue("safe")] Safe = 0,

    /// <summary>
    /// 危险操作 — 需用户确认（Auto 模式拒绝并引导，Ask 模式确认）
    /// </summary>
    [EnumValue("dangerous")] Dangerous = 1,

    /// <summary>
    /// 极危险操作 — 需用户显式确认，不可批量批准，不可"始终允许"
    /// </summary>
    [EnumValue("critical")] Critical = 2,

    /// <summary>
    /// 绝对禁止 — AI 永远拒绝执行，任何权限模式下都被拦截，引导用户在终端手动执行
    /// </summary>
    [EnumValue("forbidden")] Forbidden = 3
}
