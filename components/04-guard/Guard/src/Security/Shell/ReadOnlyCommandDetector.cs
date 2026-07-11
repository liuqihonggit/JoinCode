
namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 只读命令检测器接口 — 对齐 TS readOnlyValidation.ts
/// </summary>
public interface IReadOnlyCommandDetector
{
    /// <summary>
    /// 检查命令是否为只读命令 — 对齐 TS isCommandReadOnly
    /// </summary>
    bool IsReadOnly(ShellCommand command);

    /// <summary>
    /// 检查原始命令字符串是否只读 — 对齐 TS checkReadOnlyConstraints
    /// </summary>
    ReadOnlyCheckResult CheckReadOnlyConstraints(string command, bool compoundCommandHasCd = false);
}

/// <summary>
/// 只读检查结果 — 对齐 TS PermissionResult
/// </summary>
public sealed record ReadOnlyCheckResult(
    PermissionBehavior Behavior,
    string? Message = null);
