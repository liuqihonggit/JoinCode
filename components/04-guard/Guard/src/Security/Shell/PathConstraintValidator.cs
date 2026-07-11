
namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 路径约束验证器接口 — 对齐 TS pathValidation.ts checkPathConstraints
/// </summary>
public interface IPathConstraintValidator
{
    /// <summary>
    /// 检查命令的路径约束 — 主入口，对齐 TS checkPathConstraints
    /// </summary>
    PathConstraintResult CheckPathConstraints(
        string command,
        string workingDirectory,
        bool compoundCommandHasCd = false);

    /// <summary>
    /// 验证指定命令的路径 — 对齐 TS validateCommandPaths
    /// </summary>
    PathConstraintResult ValidateCommandPaths(
        PathCommand command,
        IReadOnlyList<string> args,
        string workingDirectory,
        bool compoundCommandHasCd = false,
        FileOperationType? operationTypeOverride = null);

    /// <summary>
    /// 检查危险删除路径 — 对齐 TS checkDangerousRemovalPaths
    /// </summary>
    PathConstraintResult CheckDangerousRemovalPaths(
        PathCommand command,
        IReadOnlyList<string> args,
        string workingDirectory);
}

/// <summary>
/// 路径约束验证结果 — 对齐 TS PermissionResult
/// </summary>
public sealed record PathConstraintResult(
    PermissionBehavior Behavior,
    string? Message = null,
    string? BlockedPath = null,
    FileOperationType? OperationType = null,
    PathCommand? Command = null);
