namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 跨平台路径转换工具 — 统一 Windows↔POSIX 路径格式转换
/// 所有路径转换逻辑的单一数据源，替代分散在各处的重复实现
/// </summary>
public static partial class PathConverter
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

    /// <summary>
    /// 扫描命令字符串中的路径片段并转换为指定格式
    /// 匹配 Windows 绝对路径（C:\...）和 POSIX 风格 Windows 路径（/c/...），排除 URL 和环境变量
    /// </summary>
    /// <param name="command">Shell 命令字符串</param>
    /// <param name="toPosix">true 转为 POSIX 格式，false 转为 Windows 格式</param>
    /// <returns>路径转换后的命令字符串</returns>
    public static string GateCommandPaths(string command, bool toPosix)
    {
        if (string.IsNullOrEmpty(command)) return command;

        var result = WindowsAbsolutePathRegex().Replace(command, m =>
        {
            var path = m.Groups[1].Value;
            var converted = toPosix ? WindowsPathToPosixPath(path) : path;
            return converted;
        });

        if (toPosix) return result;

        result = PosixWindowsPathRegex().Replace(result, m =>
        {
            var path = m.Groups[1].Value;
            var converted = PosixPathToWindowsPath(path);
            return converted;
        });

        return result;
    }

    /// <summary>
    /// 匹配 Windows 绝对路径 — 盘符 + 冒号 + 路径分隔符 + 路径内容
    /// 使用负向后顾排除 URL 协议（https:、http:、ftp: 等 — 盘符前有2+字母的属于URL协议）
    /// 捕获组1: 完整路径
    /// 示例: "C:\Users\test" 或 "D:/project/w3"
    /// </summary>
    [GeneratedRegex(@"(?<![a-zA-Z]{2,})([A-Za-z]:[/\\][^\s""'|;&]+)", RegexOptions.Compiled)]
    private static partial Regex WindowsAbsolutePathRegex();

    /// <summary>
    /// 匹配 POSIX 风格 Windows 路径 — /盘符/路径
    /// 捕获组1: 完整路径
    /// 示例: "/c/Users/test" 或 "/d/project/w3"
    /// </summary>
    [GeneratedRegex(@"(/[a-z]/[^\s""'|;&]+)", RegexOptions.Compiled)]
    private static partial Regex PosixWindowsPathRegex();
}
