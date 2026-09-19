namespace JoinCode.Abstractions.Security.Auditing;

/// <summary>
/// 命令执行审计日志接口 — 记录命令执行的关键信息用于安全审计
/// <para>
/// 用法:
/// <list type="bullet">
/// <item>无人值守模式自动执行红灯命令时记录审计日志(ADR 0012)</item>
/// <item>Bypass 模式放行非黑灯命令时记录审计日志</item>
/// <item>任何需要审计的命令执行场景</item>
/// </list>
/// </para>
/// </summary>
public interface ICommandExecutionAuditor {
    /// <summary>
    /// 记录命令执行审计日志
    /// </summary>
    /// <param name="entry">审计日志条目</param>
    void Record(CommandExecutionAuditEntry entry);
}