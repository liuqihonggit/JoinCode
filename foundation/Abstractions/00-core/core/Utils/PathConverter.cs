namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 跨平台路径转换工具 — 统一 Windows↔POSIX 路径格式转换
/// 所有路径转换逻辑的单一数据源，替代分散在各处的重复实现
/// </summary>
public static class PathConverter
{
    /// <summary>
    /// Windows 路径转 POSIX 路径
    /// <list type="bullet">
    ///   <item>C:\Users\test → /c/Users/test</item>
    ///   <item>\\server\share → //server/share</item>
    ///   <item>relative\path → relative/path</item>
    /// </list>
    /// </summary>
    public static string WindowsPathToPosixPath(string windowsPath)
    {
        if (string.IsNullOrEmpty(windowsPath)) return windowsPath;

        if (windowsPath.StartsWith("\\\\"))
            return windowsPath.Replace('\\', '/');

        if (windowsPath.Length >= 2 && char.IsLetter(windowsPath[0]) && windowsPath[1] == ':')
        {
            var drive = char.ToLowerInvariant(windowsPath[0]);
            return $"/{drive}{windowsPath[2..].Replace('\\', '/')}";
        }

        return windowsPath.Replace('\\', '/');
    }

    /// <summary>
    /// POSIX 风格 Windows 路径转 Windows 路径
    /// <list type="bullet">
    ///   <item>/c/Users/test → C:\Users\test</item>
    /// </list>
    /// </summary>
    public static string PosixPathToWindowsPath(string posixPath)
    {
        if (string.IsNullOrEmpty(posixPath)) return posixPath;

        var normalized = posixPath.Replace('\\', '/');

        if (normalized.Length > 2 && normalized[0] == '/' && normalized[2] == '/' && char.IsLetter(normalized[1]))
        {
            return $"{char.ToUpperInvariant(normalized[1])}:{normalized[2..]}".Replace('/', '\\');
        }

        return posixPath;
    }

    /// <summary>
    /// 判断路径是否看起来像 Windows 绝对路径 — 用于非 Windows 平台的转换决策
    /// </summary>
    public static bool LooksLikeWindowsPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
            return true;

        if (path.StartsWith("\\\\"))
            return true;

        return false;
    }
}
