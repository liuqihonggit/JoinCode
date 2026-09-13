namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PowerShell 破坏性命令检查器 — 检测命令是否为破坏性操作并返回警告消息
/// </summary>
[Register(typeof(IPsDestructiveCommandChecker), ServiceLifetime.Singleton)]
public sealed partial class PsDestructiveCommandChecker : ServiceEntity, IPsDestructiveCommandChecker
{
    /// <inheritdoc />
    public string? GetDestructiveCommandWarning(string command)
    {
        return PsDestructiveCommandWarning.GetDestructiveCommandWarning(command);
    }
}
