namespace Core.Agents;

/// <summary>
/// Worktree include 模式匹配器 — glob 模式编译缓存,消除复制粘贴。
/// <para>支持 * (单层通配) 和 ** (跨层通配),无 * 时按前缀/全等匹配。</para>
/// <para>静态缓存用 ImmutableDictionary+CAS,无锁读路径。</para>
/// </summary>
internal static class WorktreeIncludePatternMatcher {
    private static ImmutableDictionary<string, Regex> PatternCache = ImmutableDictionary<string, Regex>.Empty;

    /// <summary>
    /// 匹配文件路径是否符合 worktree include 模式。
    /// </summary>
    public static bool Matches(string filePath, string pattern) {
        if (pattern.Contains('*')) {
            var regexStr = "^" + Regex.Escape(pattern).Replace("\\*\\*", ".*").Replace("\\*", "[^/]*") + "$";
            var regex = GetOrAddRegex(ref PatternCache, regexStr);
            try {
                return regex.IsMatch(filePath);
            } catch {
                return false;
            }
        }
        return filePath.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
               filePath.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>无锁 CAS 获取或添加缓存的 Regex</summary>
    private static Regex GetOrAddRegex(ref ImmutableDictionary<string, Regex> cache, string key) {
        if (cache.TryGetValue(key, out var existing)) return existing;
        var regex = new Regex(key, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        while (true) {
            var current = cache;
            if (current.TryGetValue(key, out existing)) return existing;
            var updated = current.SetItem(key, regex);
            if (Interlocked.CompareExchange(ref cache, updated, current) == current) return regex;
        }
    }
}
