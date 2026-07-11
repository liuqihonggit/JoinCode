
namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Bash 工具权限检查器接口 — 对齐 TS bashPermissions.ts bashToolHasPermission
/// </summary>
public interface IBashPermissionChecker
{
    /// <summary>
    /// 检查 Bash 命令的权限 — 主入口，对齐 TS bashToolHasPermission
    /// </summary>
    BashPermissionResult CheckPermission(
        string command,
        string workingDirectory);
}

/// <summary>
/// Bash 权限检查结果 — 对齐 TS PermissionResult
/// </summary>
public sealed record BashPermissionResult(
    PermissionBehavior Behavior,
    string? Message = null,
    string? SuggestedRule = null,
    IReadOnlyList<string>? DeniedRules = null);
