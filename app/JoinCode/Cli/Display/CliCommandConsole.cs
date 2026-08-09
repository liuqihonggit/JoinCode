namespace JoinCode.Cli;

/// <summary>
/// CLI 命令控制台实现 — 包装 TerminalHelper，通过 CommandTerminal.SetConsole 注入。
/// 命令类移到 Composition 后通过 CommandTerminal 兼容类输出，最终委托到此实现。
/// </summary>
internal sealed class CliCommandConsole : JoinCode.Abstractions.Interfaces.ICommandConsole
{
    public bool IsInputRedirected => TerminalHelper.IsInputRedirected;
    public bool IsOutputRedirected => TerminalHelper.IsOutputRedirected;
    public bool IsHeadless => TerminalHelper.IsHeadless;
    public bool KeyAvailable => TerminalHelper.KeyAvailable;
    public int CursorTop => TerminalHelper.CursorTop;
    public int CursorLeft => TerminalHelper.CursorLeft;
    public ConsoleColor ForegroundColor { get => TerminalHelper.ForegroundColor; set => TerminalHelper.ForegroundColor = value; }
    public ConsoleColor BackgroundColor { get => TerminalHelper.BackgroundColor; set => TerminalHelper.BackgroundColor = value; }
    public System.IO.TextWriter Out => TerminalHelper.Out;
    public System.IO.TextReader In => TerminalHelper.In;
    public System.IO.TextWriter Error => TerminalHelper.Error;

    public void WriteLine(string message) => TerminalHelper.WriteLine(message);
    public void WriteError(string message) => TerminalHelper.WriteLine($"{TerminalColors.Error}{message}{AnsiStyleConstants.Reset}");
    public void WriteSuccess(string message) => TerminalHelper.WriteLine($"{TerminalColors.Success}{message}{AnsiStyleConstants.Reset}");
    public void WriteWarning(string message) => TerminalHelper.WriteLine($"{TerminalColors.Warning}{message}{AnsiStyleConstants.Reset}");
    public void WriteRaw(string message) => TerminalHelper.WriteRaw(message);
    public void WriteErrorRaw(string message) => TerminalHelper.WriteErrorRaw(message);
    public string? ReadLine() => TerminalHelper.ReadLine();
    public ConsoleKeyInfo ReadKey(bool intercept) => TerminalHelper.ReadKey(intercept);
    public void NewLine() => TerminalHelper.NewLine();
    public void ClearScreen() => TerminalHelper.ClearScreen();
    public int GetWidth() => TerminalHelper.GetWidth();
    public int GetHeight() => TerminalHelper.GetHeight();
    public void ResetColor() => TerminalHelper.ResetColor();
    public void SetCursorPosition(int left, int top) => TerminalHelper.SetCursorPosition(left, top);
}
