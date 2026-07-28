namespace JoinCode.Cli.Display;

public static class NumberFormatter
{
    public static string FormatCompact(long value)
    {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000.0:F1}B";
        if (value >= 1_000_000) return $"{value / 1_000_000.0:F1}M";
        if (value >= 1000) return $"{value / 1000.0:F1}k";
        return value.ToString();
    }

    public static string FormatCompact(int value) => FormatCompact((long)value);

    public static string FormatWithSeparator(long value) => value.ToString("N0");

    public static string FormatWithSeparator(int value) => FormatWithSeparator((long)value);
}
