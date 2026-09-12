namespace Tools.Shell;

/// <summary>
/// Shell 路径错误自动重试助手 — 检测路径错误 + 归一化命令路径分隔符
/// 当 shell 执行因路径分隔符混用(如 a\b\c/d)失败时,自动归一化重试一次
/// </summary>
public static class ShellPathRetryHelper
{
    /// <summary>
    /// 路径错误关键词(英文 + 中文,bash + PowerShell)
    /// </summary>
    public static readonly string[] PathErrorKeywords =
    [
        "No such file or directory",
        "No such file",
        "not found",
        "cannot find",
        "does not exist",
        "cannot access",
        "系统找不到指定的路径",
        "系统找不到指定的文件",
        "找不到指定的路径",
        "找不到指定的文件",
        "找不到路径",
        "The system cannot find the path specified",
        "The system cannot find the file specified",
        "Could not find a part of the path",
    ];

    /// <summary>
    /// 检测 ToolResult 是否为路径错误(IsError + 输出含路径错误关键词)
    /// </summary>
    public static bool IsPathError(ToolResult? result)
    {
        if (result is null || !result.IsError)
            return false;

        var text = result.GetFirstText();
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var keyword in PathErrorKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 尝试归一化命令中的路径分隔符
    /// 仅当命令同时含 \ 和 / 时才处理(混合分隔符),统一为目标分隔符
    /// </summary>
    /// <param name="command">原始命令</param>
    /// <param name="toForwardSlash">true=统一为 /(bash 风格);false=统一为 \(Windows 风格)</param>
    /// <returns>归一化后的命令;如果命令不含混合分隔符或归一化后无变化,返回 null</returns>
    public static string? TryNormalizeCommand(string command, bool toForwardSlash)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var hasBackslash = command.Contains('\\');
        var hasForwardSlash = command.Contains('/');
        if (!hasBackslash || !hasForwardSlash)
            return null;

        var normalized = toForwardSlash
            ? command.Replace('\\', '/')
            : command.Replace('/', '\\');

        return string.Equals(normalized, command, StringComparison.Ordinal) ? null : normalized;
    }
}
