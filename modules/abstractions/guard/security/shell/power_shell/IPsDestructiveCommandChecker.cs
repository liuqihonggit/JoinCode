namespace JoinCode.Abstractions.Security.Shell.PowerShell;

public interface IPsDestructiveCommandChecker {
    /// <summary>获取破坏性命令的警告信息（非破坏性命令返回 null）。</summary>
    string? GetDestructiveCommandWarning(string command);
}