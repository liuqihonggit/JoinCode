namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Shell 权限检查基础结果 — 统一 BashPermissionResult / PathConstraintResult / PsSecurityResult / SedValidationResult 的公共模式
/// 对齐 TS PermissionResult
/// </summary>
public record ShellPermissionCheckResult(
    PermissionBehavior Behavior,
    string? Message = null);
