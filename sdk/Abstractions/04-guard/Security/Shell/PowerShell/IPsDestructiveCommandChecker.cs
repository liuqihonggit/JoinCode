namespace JoinCode.Abstractions.Security.Shell.PowerShell;

public interface IPsDestructiveCommandChecker
{
    string? GetDestructiveCommandWarning(string command);
}
