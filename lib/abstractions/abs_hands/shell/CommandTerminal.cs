namespace JoinCode.Abstractions.Shell;

/// <summary>
/// 命令终端兼容类 — 提供与 CLI TerminalHelper 相同的 API 表面，
/// 通过 <see cref="Interfaces.ICommandConsole"/> 委托实现。
/// 命令类移到 Hands 后通过 global using 别名 TerminalHelper = JoinCode.Abstractions.Shell.CommandTerminal 使用，
/// 代码无需修改。CLI 启动时调用 <see cref="SetConsole"/> 注入真实实现。
/// </summary>
public static class CommandTerminal {
    private static Interfaces.ICommandConsole? _console;

    /// <summary>
    /// 真实标准输出 — 在 SetConsole 时捕获，不受 SetOut 重定向影响。
    /// 交互式提示（确认框/密码输入等）用此输出，避免被命令输出重定向吞掉。
    /// </summary>
    private static System.IO.TextWriter? _realOut;

    /// <summary>真实标准输出 — SetConsole 时捕获的原始 Console.Out，不受 SetOut 重定向影响</summary>
    public static System.IO.TextWriter RealOut => _realOut ?? System.Console.Out;

    /// <summary>设置当前控制台实现（CLI 启动时调用）</summary>
    public static void SetConsole(Interfaces.ICommandConsole? console) {
        Interlocked.CompareExchange(ref _realOut, System.Console.Out, null);
        Interlocked.Exchange(ref _console, console);
    }

    /// <summary>当前控制台 — 未设置时回退到 System.Console</summary>
    private static Interfaces.ICommandConsole Console =>
        _console ?? SystemConsoleFallback.Instance;

    /// <summary>强制交互模式</summary>
    public static bool ForceInteractive { get; set; }

    /// <summary>获取是否为无头模式。</summary>
    public static bool IsHeadless => Console.IsHeadless;
    /// <summary>获取标准输入是否被重定向。</summary>
    public static bool IsInputRedirected => Console.IsInputRedirected;
    /// <summary>获取标准输出是否被重定向。</summary>
    public static bool IsOutputRedirected => Console.IsOutputRedirected;
    /// <summary>获取是否有可用按键输入。</summary>
    public static bool KeyAvailable => Console.KeyAvailable;

    /// <summary>初始化终端。</summary>
    public static void Init() { }

    /// <summary>获取终端宽度。</summary>
    public static int GetWidth() => Console.GetWidth();
    /// <summary>获取终端高度。</summary>
    public static int GetHeight() => Console.GetHeight();

    /// <summary>写入一行文本。</summary>
    public static void WriteLine(string? text = null) {
        if (text is null) Console.NewLine();
        else Console.WriteLine(text);
    }

    /// <summary>写入空行。</summary>
    public static void NewLine() => Console.NewLine();

    /// <summary>写入原始文本。</summary>
    public static void WriteRaw(string text) => Console.WriteRaw(text);

    /// <summary>写入原始字符。</summary>
    public static void WriteRaw(char c) => Console.WriteRaw(c.ToString());

    /// <summary>写入原始字符串构建器内容。</summary>
    public static void WriteRaw(System.Text.StringBuilder sb) => Console.WriteRaw(sb.ToString());

    /// <summary>写入原始字符跨度。</summary>
    public static void WriteRaw(System.ReadOnlySpan<char> span) => Console.WriteRaw(span.ToString());

    /// <summary>读取一行输入。</summary>
    public static string ReadLine() => Console.ReadLine() ?? string.Empty;

    /// <summary>读取一个按键。</summary>
    public static ConsoleKeyInfo ReadKey(bool intercept = false) => Console.ReadKey(intercept);

    /// <summary>获取或设置前景色。</summary>
    public static ConsoleColor ForegroundColor {
        get => Console.ForegroundColor;
        set => Console.ForegroundColor = value;
    }

    /// <summary>获取或设置背景色。</summary>
    public static ConsoleColor BackgroundColor {
        get => Console.BackgroundColor;
        set => Console.BackgroundColor = value;
    }

    /// <summary>重置颜色为默认值。</summary>
    public static void ResetColor() => Console.ResetColor();

    /// <summary>清屏。</summary>
    public static void ClearScreen() => Console.ClearScreen();

    /// <summary>获取光标顶部位置。</summary>
    public static int CursorTop => Console.CursorTop;
    /// <summary>获取光标左侧位置。</summary>
    public static int CursorLeft => Console.CursorLeft;

    /// <summary>设置光标位置。</summary>
    public static void SetCursorPosition(int left, int top) => Console.SetCursorPosition(left, top);

    /// <summary>设置标准输出重定向。</summary>
    public static void SetOut(System.IO.TextWriter writer) => System.Console.SetOut(writer);

    /// <summary>
    /// 输出到真实 stdout（绕过 SetOut 重定向）— 用于交互式提示
    /// </summary>
    public static void WriteLineReal(string? text = null) {
        if (text is null) RealOut.WriteLine();
        else RealOut.WriteLine(text);
        RealOut.Flush();
    }

    /// <summary>
    /// 输出到真实 stdout（绕过 SetOut 重定向）— 用于交互式提示
    /// </summary>
    public static void WriteRawReal(string text) {
        RealOut.Write(text);
        RealOut.Flush();
    }

    /// <summary>获取标准输出。</summary>
    public static System.IO.TextWriter Out => Console.Out;
    /// <summary>获取标准输入。</summary>
    public static System.IO.TextReader In => Console.In;
    /// <summary>获取标准错误输出。</summary>
    public static System.IO.TextWriter Error => Console.Error;

    /// <summary>写入错误信息。</summary>
    public static void WriteError(string? text = null) {
        if (text is null) Console.WriteError(string.Empty);
        else Console.WriteError(text);
    }

    /// <summary>写入原始错误信息。</summary>
    public static void WriteErrorRaw(string text) => Console.WriteErrorRaw(text);

    /// <summary>获取或设置输出编码。</summary>
    public static System.Text.Encoding OutputEncoding {
        get => System.Console.OutputEncoding;
        set => System.Console.OutputEncoding = value;
    }

    public static event ConsoleCancelEventHandler CancelKeyPress {
        add => System.Console.CancelKeyPress += value;
        remove => System.Console.CancelKeyPress -= value;
    }
}

/// <summary>
/// System.Console 回退实现 — GUI 进程中未设置 ICommandConsole 时的默认行为。
/// GUI 不执行命令，此实现仅防止 NullReferenceException。
/// </summary>
internal sealed class SystemConsoleFallback : Interfaces.ICommandConsole {
    public static readonly SystemConsoleFallback Instance = new();

    /// <summary>获取标准输入是否被重定向。</summary>
    public bool IsInputRedirected => System.Console.IsInputRedirected;
    /// <summary>获取标准输出是否被重定向。</summary>
    public bool IsOutputRedirected => System.Console.IsOutputRedirected;
    /// <summary>获取是否为无头模式。</summary>
    public bool IsHeadless => System.Console.IsOutputRedirected || System.Console.IsInputRedirected;
    /// <summary>获取是否有可用按键输入。</summary>
    public bool KeyAvailable => System.Console.KeyAvailable;
    /// <summary>获取光标顶部位置。</summary>
    public int CursorTop => System.Console.CursorTop;
    /// <summary>获取光标左侧位置。</summary>
    public int CursorLeft => System.Console.CursorLeft;
    /// <summary>获取或设置前景色。</summary>
    public ConsoleColor ForegroundColor { get => System.Console.ForegroundColor; set => System.Console.ForegroundColor = value; }
    /// <summary>获取或设置背景色。</summary>
    public ConsoleColor BackgroundColor { get => System.Console.BackgroundColor; set => System.Console.BackgroundColor = value; }
    /// <summary>获取标准输出。</summary>
    public System.IO.TextWriter Out => System.Console.Out;
    /// <summary>获取标准输入。</summary>
    public System.IO.TextReader In => System.Console.In;
    /// <summary>获取标准错误输出。</summary>
    public System.IO.TextWriter Error => System.Console.Error;

    /// <summary>写入一行文本。</summary>
    public void WriteLine(string message) => System.Console.WriteLine(message);
    /// <summary>写入错误信息。</summary>
    public void WriteError(string message) => System.Console.Error.WriteLine(message);
    /// <summary>写入成功信息。</summary>
    public void WriteSuccess(string message) => System.Console.WriteLine(message);
    /// <summary>写入警告信息。</summary>
    public void WriteWarning(string message) => System.Console.WriteLine(message);
    /// <summary>写入原始文本。</summary>
    public void WriteRaw(string message) => System.Console.Write(message);
    /// <summary>写入原始错误信息。</summary>
    public void WriteErrorRaw(string message) => System.Console.Error.Write(message);
    /// <summary>读取一行输入。</summary>
    public string? ReadLine() {
        if (System.Console.IsInputRedirected) return string.Empty;
        return System.Console.ReadLine();
    }
    /// <summary>读取一个按键。</summary>
    public ConsoleKeyInfo ReadKey(bool intercept) {
        if (System.Console.IsInputRedirected) return default;
        return System.Console.ReadKey(intercept);
    }
    /// <summary>写入空行。</summary>
    public void NewLine() => System.Console.WriteLine();
    /// <summary>清屏。</summary>
    public void ClearScreen() {
        if (!System.Console.IsOutputRedirected) System.Console.Clear();
    }
    /// <summary>获取终端宽度。</summary>
    public int GetWidth() {
        try { return System.Console.WindowWidth; } catch (System.IO.IOException) { return 80; }
    }
    /// <summary>获取终端高度。</summary>
    public int GetHeight() {
        try { return System.Console.WindowHeight; } catch (System.IO.IOException) { return 24; }
    }
    /// <summary>重置颜色为默认值。</summary>
    public void ResetColor() => System.Console.ResetColor();
    /// <summary>设置光标位置。</summary>
    public void SetCursorPosition(int left, int top) => System.Console.SetCursorPosition(left, top);
}