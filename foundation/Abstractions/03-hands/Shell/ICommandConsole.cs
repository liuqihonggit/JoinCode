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

    /// <summary>换行</summary>
    void NewLine();
}
