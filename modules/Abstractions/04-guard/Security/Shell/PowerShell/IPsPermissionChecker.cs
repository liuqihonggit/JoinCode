namespace JoinCode.Abstractions.Security.Shell.PowerShell;

public interface IPsPermissionChecker
{
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
