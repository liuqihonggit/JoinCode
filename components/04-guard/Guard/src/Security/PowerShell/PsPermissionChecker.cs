namespace JoinCode.Guard.Security.PowerShell;

[Register(JoinCode.Abstractions.Attributes.ServiceLifetime.Singleton)]
public sealed partial class PsPermissionChecker : IPsPermissionChecker
{
    PsSecurityResult IPsPermissionChecker.CheckPermission(
        string command,
        string workingDirectory,
        IReadOnlyList<string> denyRules,
        IReadOnlyList<string> askRules,
        IReadOnlyList<string> allowRules,
        IReadOnlyList<string> allowedDirectories,
        IReadOnlyList<string> denyDirectories,
        bool acceptEdits)
    {
        return PsPermissions.CheckPermission(command, workingDirectory, denyRules, askRules, allowRules, allowedDirectories, denyDirectories, acceptEdits);
    }
}
