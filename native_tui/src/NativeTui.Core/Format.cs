namespace NativeTui.Core;

/// <summary>
/// 纯显示格式化 — 对齐基准 utils/format.ts 的 formatDuration/formatNumber（spinner 状态行消费）。
/// 无 Ink 依赖的叶子工具。已知边缘偏离：≥999,950 时基准 Intl compact 产生千位逗号（"1,000k"），此处不分组。
/// </summary>
public static class Format
{
    /// <summary>
    /// 毫秒时长 → 人读文本：&lt;60s 整数秒；≥60s 按日/时/分/秒分层（进位链：秒 60→分、分 60→时、时 24→日）。
    /// 对齐基准默认参数路径（无 hideTrailingZeros/mostSignificantOnly）。
    /// </summary>
    public static string Duration(long ms)
    {
        if (ms < 60_000)
        {
            return ms == 0 ? "0s" : $"{ms / 1000}s";
        }

        var days = ms / 86_400_000;
        var hours = ms % 86_400_000 / 3_600_000;
        var minutes = ms % 3_600_000 / 60_000;
        var seconds = (long)Math.Round(ms % 60_000 / 1000.0, MidpointRounding.AwayFromZero);
        if (seconds == 60)
        {
            seconds = 0;
            minutes++;
        }

        if (minutes == 60)
        {
            minutes = 0;
            hours++;
        }

        if (hours == 24)
        {
            hours = 0;
            days++;
        }

        if (days > 0)
        {
            return $"{days}d {hours}h {minutes}m";
        }

        if (hours > 0)
        {
            return $"{hours}h {minutes}m {seconds}s";
        }

        return minutes > 0 ? $"{minutes}m {seconds}s" : $"{seconds}s";
    }

    /// <summary>数字 compact 记法（&lt;1000 原样；≥1000 一位小数 + 小写单位 k/m/b/t，对齐基准 formatNumber）。</summary>
    public static string Number(long value)
    {
        if (value < 1000)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var (scaled, unit) = value switch
        {
            >= 1_000_000_000_000 => (value / 1e12, "t"),
            >= 1_000_000_000 => (value / 1e9, "b"),
            >= 1_000_000 => (value / 1e6, "m"),
            _ => (value / 1e3, "k"),
        };
        return scaled.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + unit;
    }
}
