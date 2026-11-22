namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 字符串安全操作扩展 — 避免 Unicode 代理对被截断破坏。
/// </summary>
public static class StringExtensions {
    /// <summary>
    /// 安全截断字符串到指定长度，避免破坏 Unicode 代理对。
    /// <para>若截断点落在代理对中间（高代理在 maxLen-1、低代理在 maxLen），向前退一位。</para>
    /// </summary>
    /// <param name="s">待截断字符串。</param>
    /// <param name="maxLen">最大字符数。</param>
    /// <returns>截断后的字符串（超长时追加 "..."）。</returns>
    public static string SafeTruncate(this string s, int maxLen) {
        if (s.Length <= maxLen) return s;
        if (maxLen > 0 && char.IsLowSurrogate(s[maxLen]) && char.IsHighSurrogate(s[maxLen - 1]))
            return s[..(maxLen - 1)] + "...";
        return s[..maxLen] + "...";
    }
}
