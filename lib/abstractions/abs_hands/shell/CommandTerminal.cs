namespace JoinCode.Abstractions.Shell;

/// <summary>
/// 命令终端兼容类 — 提供与 CLI TerminalHelper 相同的 API 表面，
/// 通过 <see cref="Interfaces.ICommandConsole"/> 委托实现。
/// 命令类移到 Hands 后通过 global using 别名 TerminalHelper = JoinCode.Abstractions.Shell.CommandTerminal 使用，
/// 代码无需修改。CLI 启动时调用 <see cref="SetConsole"/> 注入真实实现。
/// </summary>
public static class CommandTerminal
{
    private static Interfaces.ICommandConsole? _console;

    /// <summary>
    /// 真实标准输出 — 在 SetConsole 时捕获，不受 SetOut 重定向影响。
    /// 交互式提示（确认框/密码输入等）用此输出，避免被命令输出重定向吞掉。
    /// </summary>
    private static System.IO.TextWriter? _realOut;

    /// <summary>真实标准输出 — SetConsole 时捕获的原始 Console.Out，不受 SetOut 重定向影响</summary>
    public static System.IO.TextWriter RealOut => _realOut ?? System.Console.Out;

    /// <summary>设置当前控制台实现（CLI 启动时调用）</summary>
    public static void SetConsole(Interfaces.ICommandConsole? console)
    {
        Interlocked.CompareExchange(ref _realOut, System.Console.Out, null);
        Interlocked.Exchange(ref _console, console);
    }

    /// <summary>当前控制台 — 未设置时回退到 System.Console</summary>
    private static Interfaces.ICommandConsole Console =>
        _console ?? SystemConsoleFallback.Instance;

    /// <summary>强制交互模式</summary>
    public static bool ForceInteractive { get; set; }

    public static bool IsHeadless => Console.IsHeadless;
    public static bool IsInputRedirected => Console.IsInputRedirected;
    public static bool IsOutputRedirected => Console.IsOutputRedirected;
    public static bool KeyAvailable => Console.KeyAvailable;

    public static void Init() { }

    public static int GetWidth() => Console.GetWidth();
    public static int GetHeight() => Console.GetHeight();

    public static void WriteLine(string? text = null)
    {
        if (text is null) Console.NewLine();
        else Console.WriteLine(text);
    }

    public static void NewLine() => Console.NewLine();

    public static void WriteRaw(string text) => Console.WriteRaw(text);

    public static void WriteRaw(char c) => Console.WriteRaw(c.ToString());

    public static void WriteRaw(System.Text.StringBuilder sb) => Console.WriteRaw(sb.ToString());

    public static void WriteRaw(System.ReadOnlySpan<char> span) => Console.WriteRaw(span.ToString());

    public static string ReadLine() => Console.ReadLine() ?? string.Empty;

    public static ConsoleKeyInfo ReadKey(bool intercept = false) => Console.ReadKey(intercept);

    public static ConsoleColor ForegroundColor
    {
        get => Console.ForegroundColor;
        set => Console.ForegroundColor = value;
    }

    public static ConsoleColor BackgroundColor
    {
        get => Console.BackgroundColor;
        set => Console.BackgroundColor = value;
    }

    public static void ResetColor() => Console.ResetColor();

    public static void ClearScreen() => Console.ClearScreen();

    public static int CursorTop => Console.CursorTop;
    public static int CursorLeft => Console.CursorLeft;

    public static void SetCursorPosition(int left, int top) => Console.SetCursorPosition(left, top);

    public static void SetOut(System.IO.TextWriter writer) => System.Console.SetOut(writer);

    /// <summary>
    /// 输出到真实 stdout（绕过 SetOut 重定向）— 用于交互式提示
    /// </summary>
    public static void WriteLineReal(string? text = null)
    {
        if (text is null) RealOut.WriteLine();
        else RealOut.WriteLine(text);
        RealOut.Flush();
    }

    /// <summary>
    /// 输出到真实 stdout（绕过 SetOut 重定向）— 用于交互式提示
    /// </summary>
    public static void WriteRawReal(string text)
    {
        RealOut.Write(text);
        RealOut.Flush();
    }

    public static System.IO.TextWriter Out => Console.Out;
    public static System.IO.TextReader In => Console.In;
    public static System.IO.TextWriter Error => Console.Error;

    public static void WriteError(string? text = null)
    {
        if (text is null) Console.WriteError(string.Empty);
        else Console.WriteError(text);
    }

    public static void WriteErrorRaw(string text) => Console.WriteErrorRaw(text);

    public static System.Text.Encoding OutputEncoding
    {
        get => System.Console.OutputEncoding;
        set => System.Console.OutputEncoding = value;
    }

    public static event ConsoleCancelEventHandler CancelKeyPress
    {
        add => System.Console.CancelKeyPress += value;
        remove => System.Console.CancelKeyPress -= value;
    }
}

/// <summary>
/// System.Console 回退实现 — GUI 进程中未设置 ICommandConsole 时的默认行为。
/// GUI 不执行命令，此实现仅防止 NullReferenceException。
/// </summary>
internal sealed class SystemConsoleFallback : Interfaces.ICommandConsole
{
    public static readonly SystemConsoleFallback Instance = new();

    public bool IsInputRedirected => System.Console.IsInputRedirected;
    public bool IsOutputRedirected => System.Console.IsOutputRedirected;
    public bool IsHeadless => System.Console.IsOutputRedirected || System.Console.IsInputRedirected;
    public bool KeyAvailable => System.Console.KeyAvailable;
    public int CursorTop => System.Console.CursorTop;
    public int CursorLeft => System.Console.CursorLeft;
    public ConsoleColor ForegroundColor { get => System.Console.ForegroundColor; set => System.Console.ForegroundColor = value; }
    public ConsoleColor BackgroundColor { get => System.Console.BackgroundColor; set => System.Console.BackgroundColor = value; }
    public System.IO.TextWriter Out => System.Console.Out;
    public System.IO.TextReader In => System.Console.In;
    public System.IO.TextWriter Error => System.Console.Error;

    public void WriteLine(string message) => System.Console.WriteLine(message);
    public void WriteError(string message) => System.Console.Error.WriteLine(message);
    public void WriteSuccess(string message) => System.Console.WriteLine(message);
    public void WriteWarning(string message) => System.Console.WriteLine(message);
    public void WriteRaw(string message) => System.Console.Write(message);
    public void WriteErrorRaw(string message) => System.Console.Error.Write(message);
    public string? ReadLine()
    {
        if (System.Console.IsInputRedirected) return string.Empty;
        return System.Console.ReadLine();
    }
    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        if (System.Console.IsInputRedirected) return default;
        return System.Console.ReadKey(intercept);
    }
    public void NewLine() => System.Console.WriteLine();
    public void ClearScreen()
    {
        if (!System.Console.IsOutputRedirected) System.Console.Clear();
    }
    public int GetWidth()
    {
        try { return System.Console.WindowWidth; }
        catch (System.IO.IOException) { return 80; }
    }
    public int GetHeight()
    {
        try { return System.Console.WindowHeight; }
        catch (System.IO.IOException) { return 24; }
    }
    public void ResetColor() => System.Console.ResetColor();
    public void SetCursorPosition(int left, int top) => System.Console.SetCursorPosition(left, top);
}
