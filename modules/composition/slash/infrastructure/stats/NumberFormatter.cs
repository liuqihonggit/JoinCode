namespace JoinCode.Cli.Display;

/// <summary>
/// 数字格式化器 — 提供紧凑与带分隔符两种数字展示形式
/// </summary>
public static class NumberFormatter {
    /// <summary>
    /// 将长整数格式化为紧凑形式（k/M/B 后缀）
    /// </summary>
    /// <param name="value">待格式化的数值</param>
    /// <returns>紧凑形式的字符串表示</returns>
    public static string FormatCompact(long value) {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000.0:F1}B";
        if (value >= 1_000_000) return $"{value / 1_000_000.0:F1}M";
        if (value >= 1000) return $"{value / 1000.0:F1}k";
        return value.ToString();
    }

    /// <summary>
    /// 将整数格式化为紧凑形式（k/M/B 后缀）
    /// </summary>
    /// <param name="value">待格式化的数值</param>
    /// <returns>紧凑形式的字符串表示</returns>
    public static string FormatCompact(int value) => FormatCompact((long)value);

    /// <summary>
    /// 将长整数格式化为带千位分隔符的形式
    /// </summary>
    /// <param name="value">待格式化的数值</param>
    /// <returns>带千位分隔符的字符串表示</returns>
    public static string FormatWithSeparator(long value) => value.ToString("N0");

    /// <summary>
    /// 将整数格式化为带千位分隔符的形式
    /// </summary>
    /// <param name="value">待格式化的数值</param>
    /// <returns>带千位分隔符的字符串表示</returns>
    public static string FormatWithSeparator(int value) => FormatWithSeparator((long)value);
}