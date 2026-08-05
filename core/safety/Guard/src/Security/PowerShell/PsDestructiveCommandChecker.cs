namespace JoinCode.Guard.Security.PowerShell;

[Register(JoinCode.Abstractions.Attributes.ServiceLifetime.Singleton)]
public sealed partial class PsDestructiveCommandChecker : ServiceEntity, IPsDestructiveCommandChecker
{
    public string? GetDestructiveCommandWarning(string command)
    {
        return PsDestructiveCommandWarning.GetDestructiveCommandWarning(command);
    }
}
