namespace Infrastructure.Utils.Text;

/// <summary>
/// 通配符模式匹配器 - 支持 * 和 ? 通配符，内部缓存编译后的正则表达式
/// </summary>
public static class GlobMatcher
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 判断输入字符串是否匹配通配符模式
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <param name="pattern">通配符模式（支持 * 和 ?）</param>
    /// <returns>是否匹配</returns>
    public static bool IsMatch(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(input))
            return false;

        // 无通配符时直接比较
        if (!pattern.Contains('*', StringComparison.Ordinal) && !pattern.Contains('?', StringComparison.Ordinal))
            return input.Equals(pattern, StringComparison.OrdinalIgnoreCase);

        var regex = Cache.GetOrAdd(pattern, static p =>
        {
            var escaped = "^" + Regex.Escape(p)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal) + "$";
            return new Regex(escaped, RegexOptions.IgnoreCase);
        });

        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
