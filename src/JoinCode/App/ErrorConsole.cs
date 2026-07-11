namespace JoinCode.App;

/// <summary>
/// 独立错误渲染器 — 不依赖任何 TUI 框架，仅使用 System.Console 输出
/// 用于感知程序报错，在 TUI 未启动或崩溃时仍可工作
/// </summary>
public static class ErrorConsole
{
    private static readonly object Lock = new();

    /// <summary>渲染致命错误（红色标题 + 消息 + 堆栈）</summary>
    public static void Fatal(string message, Exception? ex = null)
    {
        lock (Lock)
        {
            Err();
            Colored("  ✖ 致命错误", System.ConsoleColor.Red);
            Err();
            Err($"  {message}");

            if (ex is not null)
            {
                Err();
                Colored("  详细信息:", System.ConsoleColor.DarkGray);
                Err($"  {ex.GetType().Name}: {ex.Message}");

                if (!string.IsNullOrEmpty(ex.StackTrace))
                {
                    Err();
                    foreach (var line in ex.StackTrace.Split('\n'))
                        Err($"  {line.Trim()}");
                }
            }

            Err();
        }
    }

    /// <summary>渲染警告（黄色标题 + 消息）</summary>
    public static void Warning(string message)
    {
        lock (Lock)
        {
            Colored("  ⚠ 警告: ", System.ConsoleColor.Yellow);
            Err(message);
        }
    }

    /// <summary>渲染 API 错误（带错误分类）</summary>
    public static void ApiError(string message, string? suggestion = null)
    {
        lock (Lock)
        {
            Err();
            Colored("  ✖ API 错误", System.ConsoleColor.Red);
            Err($"  {message}");

            if (!string.IsNullOrEmpty(suggestion))
            {
                Err();
                Colored("  建议: ", System.ConsoleColor.Cyan);
                Err(suggestion);
            }

            Err();
        }
    }

    /// <summary>渲染信息提示（灰色）</summary>
    public static void Info(string message)
    {
        lock (Lock)
        {
            Colored("  ℹ ", System.ConsoleColor.DarkGray);
            Err(message);
        }
    }

    private static void Err(string? text = null)
    {
        if (text is null) TerminalHelper.WriteError();
        else TerminalHelper.WriteError(text);
    }

    private static void Colored(string text, System.ConsoleColor color)
    {
        var prev = TerminalHelper.ForegroundColor;
        try
        {
            TerminalHelper.ForegroundColor = color;
            TerminalHelper.WriteErrorRaw(text);
        }
        finally
        {
            TerminalHelper.ForegroundColor = prev;
        }
    }
}
