namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 路径归一化工具 — 统一处理路径分隔符(\ /)、尾分隔符、叶子名提取
/// 替代散落各处的私有 NormalizePath 方法,确保全项目路径处理一致
/// </summary>
public static class PathNormalizer
{
    /// <summary>
    /// 去除路径首尾空白和尾部分隔符(\ /),不解析为绝对路径
    /// </summary>
    public static string TrimTrailingSeparators(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        return path.Trim().TrimEnd('/', '\\');
    }

    /// <summary>
    /// 获取叶子名(文件/目录名),跨 \ / 分隔符,先去尾分隔符再取
    /// </summary>
    public static string GetLeafName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        var trimmed = path.TrimEnd('/', '\\');
        return System.IO.Path.GetFileName(trimmed);
    }

    /// <summary>
    /// 归一化路径:Trim + GetFullPath + 去尾分隔符,异常时回退到 Trim + 去尾分隔符
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        try
        {
            return System.IO.Path.GetFullPath(path.Trim())
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return path.Trim()
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
    }

    /// <summary>
    /// 归一化后 Ordinal 比对(大小写敏感)
    /// </summary>
    public static bool EqualsOrdinal(string? a, string? b)
        => string.Equals(Normalize(a ?? string.Empty), Normalize(b ?? string.Empty), StringComparison.Ordinal);

    /// <summary>
    /// 归一化后 OrdinalIgnoreCase 比对(大小写不敏感)
    /// </summary>
    public static bool EqualsIgnoreCase(string? a, string? b)
        => string.Equals(Normalize(a ?? string.Empty), Normalize(b ?? string.Empty), StringComparison.OrdinalIgnoreCase);
}
