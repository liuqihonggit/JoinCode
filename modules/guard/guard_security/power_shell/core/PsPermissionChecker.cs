namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PowerShell 权限检查器 — 委托给 <see cref="PsPermissions"/> 执行命令权限决策
/// </summary>
[Register(typeof(IPsPermissionChecker), ServiceLifetime.Singleton)]
public sealed partial class PsPermissionChecker : ServiceEntity, IPsPermissionChecker {
    /// <inheritdoc />
    PsSecurityResult IPsPermissionChecker.CheckPermission(
        string command,
        string workingDirectory,
        IReadOnlyList<string> denyRules,
        IReadOnlyList<string> askRules,
        IReadOnlyList<string> allowRules,
        IReadOnlyList<string> allowedDirectories,
        IReadOnlyList<string> denyDirectories,
        bool acceptEdits) {
        return PsPermissions.CheckPermission(command, workingDirectory, denyRules, askRules, allowRules, allowedDirectories, denyDirectories, acceptEdits);
    }
}