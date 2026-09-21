namespace JoinCode.Cli;

/// <summary>
/// CLI 命令控制台实现 — 包装 TerminalHelper，通过 CommandTerminal.SetConsole 注入。
/// 命令类移到 Composition 后通过 CommandTerminal 兼容类输出，最终委托到此实现。
/// </summary>
internal sealed class CliCommandConsole : JoinCode.Abstractions.Interfaces.ICommandConsole {
    /// <summary>标准输入是否被重定向</summary>
    public bool IsInputRedirected => TerminalHelper.IsInputRedirected;
    /// <summary>标准输出是否被重定向</summary>
    public bool IsOutputRedirected => TerminalHelper.IsOutputRedirected;
    /// <summary>是否为无头模式（输入或输出被重定向且未强制交互）</summary>
    public bool IsHeadless => TerminalHelper.IsHeadless;
    /// <summary>是否有按键可读</summary>
    public bool KeyAvailable => TerminalHelper.KeyAvailable;
    /// <summary>光标行位置（顶部坐标）</summary>
    public int CursorTop => TerminalHelper.CursorTop;
    /// <summary>光标列位置（左侧坐标）</summary>
    public int CursorLeft => TerminalHelper.CursorLeft;
    /// <summary>前景色 — 转发到 TerminalHelper.ForegroundColor</summary>
    public ConsoleColor ForegroundColor { get => TerminalHelper.ForegroundColor; set => TerminalHelper.ForegroundColor = value; }
    /// <summary>背景色 — 转发到 TerminalHelper.BackgroundColor</summary>
    public ConsoleColor BackgroundColor { get => TerminalHelper.BackgroundColor; set => TerminalHelper.BackgroundColor = value; }
    /// <summary>标准输出写入器</summary>
    public System.IO.TextWriter Out => TerminalHelper.Out;
    /// <summary>标准输入读取器</summary>
    public System.IO.TextReader In => TerminalHelper.In;
    /// <summary>标准错误写入器</summary>
    public System.IO.TextWriter Error => TerminalHelper.Error;

    /// <summary>写入一行文本并换行</summary>
    /// <param name="message">要写入的文本</param>
    public void WriteLine(string message) => TerminalHelper.WriteLine(message);
    /// <summary>写入错误级别文本（带错误色前缀）</summary>
    /// <param name="message">要写入的错误文本</param>
    public void WriteError(string message) => TerminalHelper.WriteError($"{TerminalColors.Error}{message}{AnsiStyleEnumConstants.Reset}");
    /// <summary>写入成功级别文本（带成功色前缀）</summary>
    /// <param name="message">要写入的成功文本</param>
    public void WriteSuccess(string message) => TerminalHelper.WriteLine($"{TerminalColors.Success}{message}{AnsiStyleEnumConstants.Reset}");
    /// <summary>写入警告级别文本（带警告色前缀）</summary>
    /// <param name="message">要写入的警告文本</param>
    public void WriteWarning(string message) => TerminalHelper.WriteError($"{TerminalColors.Warning}{message}{AnsiStyleEnumConstants.Reset}");
    /// <summary>原始写入字符串（不换行、不带颜色）</summary>
    /// <param name="message">要写入的文本</param>
    public void WriteRaw(string message) => TerminalHelper.WriteRaw(message);
    /// <summary>原始写入错误流（不换行、不带颜色）</summary>
    /// <param name="message">要写入的错误文本</param>
    public void WriteErrorRaw(string message) => TerminalHelper.WriteErrorRaw(message);
    /// <summary>读取一行输入</summary>
    /// <returns>读取到的行（EOF 时为 null）</returns>
    public string? ReadLine() => TerminalHelper.ReadLine();
    /// <summary>读取按键</summary>
    /// <param name="intercept">是否拦截按键（不显示到输出）</param>
    /// <returns>按键信息</returns>
    public ConsoleKeyInfo ReadKey(bool intercept) => TerminalHelper.ReadKey(intercept);
    /// <summary>写入空行</summary>
    public void NewLine() => TerminalHelper.NewLine();
    /// <summary>清屏</summary>
    public void ClearScreen() => TerminalHelper.ClearScreen();
    /// <summary>获取终端宽度（列数）</summary>
    /// <returns>终端列数</returns>
    public int GetWidth() => TerminalHelper.GetWidth();
    /// <summary>获取终端高度（行数）</summary>
    /// <returns>终端行数</returns>
    public int GetHeight() => TerminalHelper.GetHeight();
    /// <summary>重置控制台颜色到默认值</summary>
    public void ResetColor() => TerminalHelper.ResetColor();
    /// <summary>设置光标位置</summary>
    /// <param name="left">列坐标</param>
    /// <param name="top">行坐标</param>
    public void SetCursorPosition(int left, int top) => TerminalHelper.SetCursorPosition(left, top);
}