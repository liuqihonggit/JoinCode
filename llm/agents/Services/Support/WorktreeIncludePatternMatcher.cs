namespace Core.Agents;

/// <summary>
/// Worktree include 模式匹配器 — glob 模式编译缓存,消除复制粘贴。
/// <para>支持 * (单层通配) 和 ** (跨层通配),无 * 时按前缀/全等匹配。</para>
/// </summary>
internal static class WorktreeIncludePatternMatcher
{
    private static readonly ConcurrentDictionary<string, Regex> s_patternCache = new(StringComparer.Ordinal);

    /// <summary>
    /// 匹配文件路径是否符合 worktree include 模式。
    /// </summary>
    public static bool Matches(string filePath, string pattern)
    {
        if (pattern.Contains('*'))
        {
            var regexStr = "^" + Regex.Escape(pattern).Replace("\\*\\*", ".*").Replace("\\*", "[^/]*") + "$";
            var regex = s_patternCache.GetOrAdd(regexStr, static r => new Regex(r, RegexOptions.IgnoreCase | RegexOptions.Compiled));
            try
            {
                return regex.IsMatch(filePath);
            }
            catch
            {
                return false;
            }
        }
        return filePath.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
               filePath.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
