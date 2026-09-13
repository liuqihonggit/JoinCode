namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 命令控制台抽象 — 解耦命令类对 CLI TerminalHelper 的直接依赖。
/// CLI 中实现（包装 TerminalHelper），通过 DI 注入或静态设置。
/// 命令类移到共享库后通过此接口输出，不直接引用 CLI。
/// </summary>
public interface ICommandConsole
{
    /// <summary>输出一行文本</summary>
    void WriteLine(string message);

    /// <summary>输出错误文本（红色前缀）</summary>
    void WriteError(string message);

    /// <summary>输出成功文本（绿色前缀）</summary>
    void WriteSuccess(string message);

    /// <summary>输出警告文本（黄色前缀）</summary>
    void WriteWarning(string message);

    /// <summary>输出原始文本（无颜色处理）</summary>
    void WriteRaw(string message);

    /// <summary>读取一行输入</summary>
    string? ReadLine();

    /// <summary>读取一个按键</summary>
    ConsoleKeyInfo ReadKey(bool intercept);

    /// <summary>输入是否被重定向（非交互模式）</summary>
    bool IsInputRedirected { get; }

    /// <summary>输出是否被重定向</summary>
    bool IsOutputRedirected { get; }

    /// <summary>换行</summary>
    void NewLine();

    /// <summary>清屏</summary>
    void ClearScreen();

    /// <summary>获取终端宽度</summary>
    int GetWidth();

    /// <summary>获取终端高度</summary>
    int GetHeight();

    /// <summary>是否无头模式（输出或输入重定向）</summary>
    bool IsHeadless { get; }

    /// <summary>是否有按键可用</summary>
    bool KeyAvailable { get; }

    /// <summary>前景色</summary>
    ConsoleColor ForegroundColor { get; set; }

    /// <summary>背景色</summary>
    ConsoleColor BackgroundColor { get; set; }

    /// <summary>重置颜色</summary>
    void ResetColor();

    /// <summary>光标行</summary>
    int CursorTop { get; }

    /// <summary>光标列</summary>
    int CursorLeft { get; }

    /// <summary>设置光标位置</summary>
    void SetCursorPosition(int left, int top);

    /// <summary>输出错误原始文本</summary>
    void WriteErrorRaw(string message);

    /// <summary>标准输出</summary>
    System.IO.TextWriter Out { get; }

    /// <summary>标准输入</summary>
    System.IO.TextReader In { get; }

    /// <summary>错误输出</summary>
    System.IO.TextWriter Error { get; }
}
