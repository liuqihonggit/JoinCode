
namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// 记忆新鲜度提示
/// 对齐 TS: memdir/memoryAge.ts — memoryFreshnessNote + memoryFreshnessText + memoryAge
/// 在读取记忆文件时，如果文件超过1天，在输出前加一个 system-reminder 提示记忆可能过时
/// </summary>
public static class MemoryFreshnessNote
{
    /// <summary>
    /// 一天的毫秒数
    /// </summary>
    private const long MillisecondsPerDay = 86_400_000L;

    /// <summary>
    /// 计算记忆年龄（天数）
    /// 对齐 TS: memoryAgeDays — 向下取整，0=今天，1=昨天，2+=更早
    /// </summary>
    public static int MemoryAgeDays(long mtimeMs)
    {
        var ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - mtimeMs;
        return Math.Max(0, (int)(ageMs / MillisecondsPerDay));
    }

    /// <summary>
    /// 人类可读的年龄字符串
    /// 对齐 TS: memoryAge — "today" / "yesterday" / "N days ago"
    /// </summary>
    public static string MemoryAge(long mtimeMs)
    {
        var days = MemoryAgeDays(mtimeMs);
        if (days == 0) return "today";
        if (days == 1) return "yesterday";
        return $"{days} days ago";
    }

    /// <summary>
    /// 纯文本过时警告（超过1天的记忆）
    /// 对齐 TS: memoryFreshnessText — 新鲜记忆返回空字符串
    /// </summary>
    public static string FreshnessText(long mtimeMs)
    {
        var days = MemoryAgeDays(mtimeMs);
        if (days <= 1) return string.Empty;
        return $"This memory is {days} days old. " +
               "Memories are point-in-time observations, not live state — " +
               "claims about code behavior or file:line citations may be outdated. " +
               "Verify against current code before asserting as fact.";
    }

    /// <summary>
    /// 包装在 system-reminder 标签中的过时提示
    /// 对齐 TS: memoryFreshnessNote — 用于 FileReadTool 输出
    /// 新鲜记忆（≤1天）返回空字符串
    /// </summary>
    public static string FreshnessNote(long mtimeMs)
    {
        var text = FreshnessText(mtimeMs);
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return $"<system-reminder>{text}</system-reminder>\n";
    }

    /// <summary>
    /// 检查文件路径是否为记忆文件
    /// 对齐 TS: isAutoMemFile — 检查路径是否在记忆目录下
    /// 记忆目录: ~/.jcc/memories/ 或 ~/.jcc/rules/ 或项目下的 .jcc/rules/
    /// </summary>
    public static bool IsMemoryFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var normalizedPath = Path.GetFullPath(filePath);

        // ~/.jcc/memories/ 目录下的文件
        var autoMemDir = Path.Combine(homeDir, AppDataConstants.AppDataFolder, "memories");
        if (normalizedPath.StartsWith(autoMemDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;

        // ~/.jcc/rules/ 目录下的文件
        var rulesDir = Path.Combine(homeDir, AppDataConstants.AppDataFolder, AppDataConstants.RulesFolderName);
        if (normalizedPath.StartsWith(rulesDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;

        // 项目下的 .jcc/rules/ 目录
        var cwd = Environment.CurrentDirectory;
        var projectRulesDir = Path.Combine(cwd, AppDataConstants.AppDataFolder, AppDataConstants.RulesFolderName);
        if (normalizedPath.StartsWith(projectRulesDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
