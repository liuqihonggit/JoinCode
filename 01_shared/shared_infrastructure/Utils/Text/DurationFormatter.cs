namespace Infrastructure.Utils.Text;

public sealed class DurationFormatOptions
{
    public bool HideTrailingZeros { get; init; } = true;
    public bool MostSignificantOnly { get; init; }
    public bool UseAbbreviations { get; init; } = true;

    public static DurationFormatOptions Default { get; } = new();
    public static DurationFormatOptions Verbose { get; } = new() { HideTrailingZeros = false };
    public static DurationFormatOptions Compact { get; } = new();
    public static DurationFormatOptions MostSignificant { get; } = new() { MostSignificantOnly = true };
}

public static class DurationFormatter
{
    public static string Format(TimeSpan duration, DurationFormatOptions? options = null)
    {
        options ??= DurationFormatOptions.Default;

        if (duration == TimeSpan.Zero) return options.UseAbbreviations ? "0s" : "0秒";

        if (options.MostSignificantOnly)
        {
            return FormatMostSignificant(duration, options);
        }

        return FormatFull(duration, options);
    }

    public static string Format(long milliseconds, DurationFormatOptions? options = null)
    {
        return Format(TimeSpan.FromMilliseconds(milliseconds), options);
    }

    private static string FormatFull(TimeSpan duration, DurationFormatOptions options)
    {
        if (options.UseAbbreviations)
        {
            return FormatFullAbbreviated(duration, options);
        }

        return FormatFullChinese(duration, options);
    }

    private static string FormatFullAbbreviated(TimeSpan duration, DurationFormatOptions options)
    {
        var totalMs = duration.TotalMilliseconds;

        if (totalMs < 1000) return $"{(int)totalMs}ms";

        if (totalMs < 60000)
        {
            var totalSeconds = totalMs / 1000.0;
            if (totalSeconds == Math.Floor(totalSeconds))
                return $"{(int)totalSeconds}s";
            return totalSeconds < 10 ? $"{totalSeconds:F1}s" : $"{(int)totalSeconds}s";
        }

        var parts = new List<string>();
        var days = (int)duration.TotalDays;
        var hours = duration.Hours;
        var minutes = duration.Minutes;
        var seconds = duration.Seconds;

        if (days > 0)
        {
            parts.Add($"{days}d");
            parts.Add($"{hours}h");
            if (!options.HideTrailingZeros || minutes > 0)
                parts.Add($"{minutes}m");
        }
        else if (hours > 0)
        {
            parts.Add($"{hours}h");
            if (!options.HideTrailingZeros || minutes > 0)
                parts.Add($"{minutes}m");
            if (!options.HideTrailingZeros || seconds > 0)
                parts.Add($"{seconds}s");
        }
        else
        {
            parts.Add($"{minutes}m");
            if (!options.HideTrailingZeros || seconds > 0)
                parts.Add($"{seconds}s");
        }

        return string.Join(" ", parts);
    }

    private static string FormatFullChinese(TimeSpan duration, DurationFormatOptions options)
    {
        var parts = new List<string>();
        var days = (int)duration.TotalDays;
        var hours = duration.Hours;
        var minutes = duration.Minutes;
        var seconds = duration.Seconds;

        if (days > 0)
        {
            parts.Add($"{days}天");
            if (!options.HideTrailingZeros || hours > 0)
                parts.Add($"{hours}小时");
        }
        else if (hours > 0)
        {
            parts.Add($"{hours}小时");
            if (!options.HideTrailingZeros || minutes > 0)
                parts.Add($"{minutes}分钟");
        }
        else if (minutes > 0)
        {
            parts.Add($"{minutes}分钟");
            if (!options.HideTrailingZeros || seconds > 0)
                parts.Add($"{seconds}秒");
        }
        else
        {
            parts.Add($"{seconds}秒");
        }

        return string.Join("", parts);
    }

    private static string FormatMostSignificant(TimeSpan duration, DurationFormatOptions options)
    {
        if (options.UseAbbreviations)
        {
            if (duration.TotalHours >= 1) return $"{duration.TotalHours:F1}h";
            if (duration.TotalMinutes >= 1) return $"{duration.TotalMinutes:F1}m";
            if (duration.TotalSeconds >= 1) return $"{duration.TotalSeconds:F1}s";
            return $"{duration.TotalMilliseconds:F0}ms";
        }

        if (duration.TotalHours >= 1) return $"{duration.TotalHours:F1}小时";
        if (duration.TotalMinutes >= 1) return $"{duration.TotalMinutes:F1}分钟";
        if (duration.TotalSeconds >= 1) return $"{duration.TotalSeconds:F1}秒";
        return $"{duration.TotalMilliseconds:F0}毫秒";
    }
}