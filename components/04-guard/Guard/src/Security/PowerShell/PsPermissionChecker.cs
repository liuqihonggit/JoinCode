namespace JoinCode.Guard.Security.PowerShell;

[Register(JoinCode.Abstractions.Attributes.ServiceLifetime.Singleton)]
public sealed class PsPermissionChecker : IPsPermissionChecker
{
    AbstractionsPsSecurityResult IPsPermissionChecker.CheckPermission(
        string command,
        string workingDirectory,
        IReadOnlyList<string> denyRules,
        IReadOnlyList<string> askRules,
        IReadOnlyList<string> allowRules,
        IReadOnlyList<string> allowedDirectories,
        IReadOnlyList<string> denyDirectories,
        bool acceptEdits)
    {
        var result = PsPermissions.CheckPermission(command, workingDirectory, denyRules, askRules, allowRules, allowedDirectories, denyDirectories, acceptEdits);
        return result.ToAbstractionsResult();
    }
}
