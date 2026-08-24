namespace JoinCode.Guard.Security.PowerShell;

[Register(typeof(IPsDestructiveCommandChecker), ServiceLifetime.Singleton)]
public sealed partial class PsDestructiveCommandChecker : ServiceEntity, IPsDestructiveCommandChecker
{
    public string? GetDestructiveCommandWarning(string command)
    {
        return PsDestructiveCommandWarning.GetDestructiveCommandWarning(command);
    }
}
