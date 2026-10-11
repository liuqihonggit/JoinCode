namespace JoinCode.Abstractions.Security.Shell.PowerShell;

public interface IPsPermissionChecker {
    /// <summary>检查 PowerShell 命令权限。</summary>
    PsSecurityResult CheckPermission(
        string command,
        string workingDirectory,
        IReadOnlyList<string> denyRules,
        IReadOnlyList<string> askRules,
        IReadOnlyList<string> allowRules,
        IReadOnlyList<string> allowedDirectories,
        IReadOnlyList<string> denyDirectories,
        bool acceptEdits = false);
}