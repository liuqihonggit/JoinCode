namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 路径归一化工具 — 统一处理路径分隔符(\ /)、尾分隔符、叶子名提取
/// 替代散落各处的私有 NormalizePath 方法,确保全项目路径处理一致
/// </summary>
public static class PathNormalizer {
    /// <summary>
    /// 去除路径首尾空白和尾部分隔符(\ /),不解析为绝对路径
    /// </summary>
    public static string TrimTrailingSeparators(string path) {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        return path.Trim().TrimEnd('/', '\\');
    }

    /// <summary>
    /// 获取叶子名(文件/目录名),跨 \ / 分隔符,先去尾分隔符再取
    /// <para>不依赖 Path.GetFileName — Linux 上 Path.GetFileName 不识别 \ 分隔符</para>
    /// </summary>
    public static string GetLeafName(string path) {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        var trimmed = path.TrimEnd('/', '\\');
        var lastSep = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return lastSep >= 0 ? trimmed[(lastSep + 1)..] : trimmed;
    }

    /// <summary>
    /// 归一化路径:Trim + 统一分隔符为平台分隔符 + 去尾分隔符
    /// <para>不依赖 Path.GetFullPath — Linux 上 GetFullPath 不识别 \ 分隔符,且会引入工作目录副作用</para>
    /// </summary>
    public static string Normalize(string path) {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        var sep = System.IO.Path.DirectorySeparatorChar;
        return path.Trim().Replace('\\', sep).Replace('/', sep).TrimEnd(sep);
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