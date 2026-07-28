
namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PowerShell 破坏性命令警告检测。
/// 检测潜在的破坏性 PowerShell 命令并返回警告字符串用于权限对话框显示。
/// 纯信息性 — 不影响权限逻辑或自动审批。
/// 对齐 TS: src/tools/PowerShellTool/destructiveCommandWarning.ts
/// </summary>
public static class PsDestructiveCommandWarning
{
    /// <summary>
    /// 破坏性模式定义
    /// </summary>
    private sealed record DestructivePattern(Regex Pattern, string Warning);

    /// <summary>
    /// PS 专用破坏性命令模式列表。
    /// 锚定到语句起始，避免误匹配。
    /// </summary>
    private static readonly DestructivePattern[] Patterns = BuildPatterns();

    private static DestructivePattern[] BuildPatterns()
    {
        return
        [
            // Remove-Item 带 -Recurse 和/或 -Force（含常见别名 rm/del/rd/rmdir/ri）
            // 锚定到语句起始，避免 git rm --force 误匹配
            new(new(@"(?:^|[|;&\n({])\s*(Remove-Item|rm|del|rd|rmdir|ri)\b[^|;&\n}]*-Recurse\b[^|;&\n}]*-Force\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may recursively force-remove files"),
            new(new(@"(?:^|[|;&\n({])\s*(Remove-Item|rm|del|rd|rmdir|ri)\b[^|;&\n}]*-Force\b[^|;&\n}]*-Recurse\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may recursively force-remove files"),
            new(new(@"(?:^|[|;&\n({])\s*(Remove-Item|rm|del|rd|rmdir|ri)\b[^|;&\n}]*-Recurse\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may recursively remove files"),
            new(new(@"(?:^|[|;&\n({])\s*(Remove-Item|rm|del|rd|rmdir|ri)\b[^|;&\n}]*-Force\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may force-remove files"),

            // Clear-Content 通配路径
            new(new(@"\bClear-Content\b[^|;&\n]*\*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may clear content of multiple files"),

            // Format-Volume 和 Clear-Disk
            new(new(@"\bFormat-Volume\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may format a disk volume"),
            new(new(@"\bClear-Disk\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may clear a disk"),

            // Git 破坏性操作（与 BashTool 相同）
            new(new(@"\bgit\s+reset\s+--hard\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may discard uncommitted changes"),
            new(new(@"\bgit\s+push\b[^|;&\n]*\s+(--force|--force-with-lease|-f)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may overwrite remote history"),
            new(new(@"\bgit\s+clean\b(?![^|;&\n]*(?:-[a-zA-Z]*n|--dry-run))[^|;&\n]*-[a-zA-Z]*f", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may permanently delete untracked files"),
            new(new(@"\bgit\s+stash\s+(drop|clear)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may permanently remove stashed changes"),

            // 数据库操作
            new(new(@"\b(DROP|TRUNCATE)\s+(TABLE|DATABASE|SCHEMA)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: may drop or truncate database objects"),

            // 系统操作
            new(new(@"\bStop-Computer\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: will shut down the computer"),
            new(new(@"\bRestart-Computer\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: will restart the computer"),
            new(new(@"\bClear-RecycleBin\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                "Note: permanently deletes recycled files"),
        ];
    }

    /// <summary>
    /// 检查 PowerShell 命令是否匹配已知的破坏性模式。
    /// 返回人类可读的警告字符串，如果未检测到破坏性模式则返回 null。
    /// </summary>
    public static string? GetDestructiveCommandWarning(string command)
    {
        if (string.IsNullOrEmpty(command)) return null;

        foreach (var pattern in Patterns)
        {
            if (pattern.Pattern.IsMatch(command))
            {
                return pattern.Warning;
            }
        }

        return null;
    }
}
