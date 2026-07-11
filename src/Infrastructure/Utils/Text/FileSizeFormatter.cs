
namespace Core.Utils;

/// <summary>
/// 文件大小格式化器
/// </summary>
public static class FileSizeFormatter
{
    private const long KB = 1024;
    private const long MB = KB * 1024;
    private const long GB = MB * 1024;
    private const long TB = GB * 1024;

    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>
    /// 格式化文件大小为人类可读格式
    /// </summary>
    public static string Format(long bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "字节数不能为负数");

        if (bytes == 0)
            return "0 B";

        // 使用Span避免字符串分配
        Span<char> buffer = stackalloc char[32];
        int charsWritten;

        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < Units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        // 格式化数值
        if (value >= 100)
        {
            // 大数值：整数
            if (!value.TryFormat(buffer, out charsWritten, "F0"))
                return FallbackFormat(bytes);
        }
        else if (value >= 10)
        {
            // 中等数值：1位小数
            if (!value.TryFormat(buffer, out charsWritten, "F1"))
                return FallbackFormat(bytes);
        }
        else
        {
            // 小数值：2位小数
            if (!value.TryFormat(buffer, out charsWritten, "F2"))
                return FallbackFormat(bytes);
        }

        // 添加单位和空格
        buffer[charsWritten] = ' ';
        charsWritten++;

        var unit = Units[unitIndex];
        for (int i = 0; i < unit.Length; i++)
        {
            buffer[charsWritten + i] = unit[i];
        }
        charsWritten += unit.Length;

        return new string(buffer[..charsWritten]);
    }

    /// <summary>
    /// 格式化文件大小（快速路径，适用于已知范围）
    /// </summary>
    public static string FormatFast(long bytes)
    {
        return bytes switch
        {
            >= TB => $"{bytes / (double)TB:F2} TB",
            >= GB => $"{bytes / (double)GB:F2} GB",
            >= MB => $"{bytes / (double)MB:F2} MB",
            >= KB => $"{bytes / (double)KB:F2} KB",
            _ => $"{bytes} B"
        };
    }

    /// <summary>
    /// 尝试格式化到Span
    /// </summary>
    public static bool TryFormat(long bytes, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;

        if (destination.IsEmpty)
            return false;

        try
        {
            var formatted = Format(bytes);
            if (formatted.Length > destination.Length)
                return false;

            formatted.AsSpan().CopyTo(destination);
            charsWritten = formatted.Length;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FallbackFormat(long bytes)
    {
        return bytes switch
        {
            >= TB => $"{bytes / (double)TB:F2} TB",
            >= GB => $"{bytes / (double)GB:F2} GB",
            >= MB => $"{bytes / (double)MB:F2} MB",
            >= KB => $"{bytes / (double)KB:F2} KB",
            _ => $"{bytes} B"
        };
    }
}
