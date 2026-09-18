namespace JoinCode.Cli;

// ─── AnsiEscape ───

/// <summary>
/// ANSI 转义序列辅助 — CLI 简化版
/// </summary>
public static class AnsiEscape
{
    /// <summary>
    /// 生成光标上移 ANSI 转义序列
    /// </summary>
    /// <param name="count">上移行数</param>
    /// <returns>ANSI 转义序列，count 小于等于 0 时返回空字符串</returns>
    public static string CursorUp(int count) => count > 0 ? $"\x1b[{count}A" : "";

    /// <summary>
    /// 生成光标下移 ANSI 转义序列
    /// </summary>
    /// <param name="count">下移行数</param>
    /// <returns>ANSI 转义序列，count 小于等于 0 时返回空字符串</returns>
    public static string CursorDown(int count) => count > 0 ? $"\x1b[{count}B" : "";

    /// <summary>
    /// 生成光标左移 ANSI 转义序列
    /// </summary>
    /// <param name="count">左移列数</param>
    /// <returns>ANSI 转义序列，count 小于等于 0 时返回空字符串</returns>
    public static string CursorLeft(int count) => count > 0 ? $"\x1b[{count}D" : "";

    /// <summary>
    /// 生成光标右移 ANSI 转义序列
    /// </summary>
    /// <param name="count">右移列数</param>
    /// <returns>ANSI 转义序列，count 小于等于 0 时返回空字符串</returns>
    public static string CursorRight(int count) => count > 0 ? $"\x1b[{count}C" : "";

    /// <summary>
    /// 从光标位置清除到屏幕末尾的 ANSI 转义序列
    /// </summary>
    public static string ClearScreenFromCursor => "\x1b[J";

    /// <summary>
    /// 清除当前行的 ANSI 转义序列
    /// </summary>
    public static string ClearLine => "\x1b[2K";
}
