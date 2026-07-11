namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Shell 解释器类型
/// </summary>
public enum ShellType
{
    [EnumValue("bash")] Bash,
    [EnumValue("powershell")] PowerShell,
    [EnumValue("cmd")] Cmd
}

/// <summary>
/// ShellType 扩展 — 补充 pwsh 别名映射
/// </summary>
public static class ShellTypeHelper
{
    /// <summary>
    /// 从字符串解析 ShellType（支持 pwsh 作为 PowerShell 别名）
    /// </summary>
    public static ShellType? ParseShellType(string? value)
    {
        if (value is null) return null;
        var result = ShellTypeExtensions.FromValue(value);
        if (result is not null) return result;

        // 别名: pwsh → PowerShell
        return value.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            ? ShellType.PowerShell
            : null;
    }
}
