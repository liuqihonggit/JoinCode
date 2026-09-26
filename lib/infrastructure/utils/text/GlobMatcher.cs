namespace Infrastructure.Utils.Text;

/// <summary>
/// 通配符模式匹配器 - 支持 * 和 ? 通配符，内部缓存编译后的正则表达式
/// </summary>
public static class GlobMatcher {
    private static ImmutableDictionary<string, Regex> _cache = ImmutableDictionary<string, Regex>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 判断输入字符串是否匹配通配符模式
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <param name="pattern">通配符模式（支持 * 和 ?）</param>
    /// <returns>是否匹配</returns>
    public static bool IsMatch(string input, string pattern) {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(input))
            return false;

        // 无通配符时直接比较
        if (!pattern.Contains('*', StringComparison.Ordinal) && !pattern.Contains('?', StringComparison.Ordinal))
            return input.Equals(pattern, StringComparison.OrdinalIgnoreCase);

        var regex = GetOrAddRegex(pattern);

        try {
            return regex.IsMatch(input);
        } catch (RegexMatchTimeoutException) {
            return false;
        }
    }

    private static Regex GetOrAddRegex(string pattern) {
        var snapshot = Volatile.Read(ref _cache);
        if (snapshot.TryGetValue(pattern, out var existing))
            return existing;

        var escaped = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        var compiled = new Regex(escaped, RegexOptions.IgnoreCase);

        ImmutableInterlocked.Update(ref _cache, d => d.ContainsKey(pattern) ? d : d.Add(pattern, compiled));
        return Volatile.Read(ref _cache)[pattern];
    }
}