namespace JoinCode.Cli;

/// <summary>
/// 终端辅助 — 纯 CLI 模式下的控制台 I/O 封装
/// </summary>
public static class TerminalHelper
{
    private static bool _isInitialized;

    /// <summary>
    /// 是否禁用颜色输出 — 由 NO_COLOR 环境变量或 CliModeDetector 控制
    /// 对齐架构指南：检测到 NO_COLOR 时自动降级为零着色模式
    /// </summary>
    private static bool _noColor;

    /// <summary>
    /// 真实标准输出 — 在 Init() 时捕获，不受 SetOut 重定向影响。
    /// 交互式提示（确认框/密码输入等）用此输出，避免被命令输出重定向吞掉。
    /// </summary>
    private static TextWriter? _realOut;

    /// <summary>
    /// 真实标准输出 — Init() 时捕获的原始 Console.Out，不受 SetOut 重定向影响
    /// </summary>
    public static TextWriter RealOut => _realOut ?? System.Console.Out;

    /// <summary>
    /// 强制交互模式 — 即使 stdin 重定向也从 Console.In 读取输入，用于 E2E 测试
    /// </summary>
    public static bool ForceInteractive { get; set; }

    public static bool IsHeadless => !ForceInteractive && (System.Console.IsOutputRedirected || System.Console.IsInputRedirected);

    public static bool IsInputRedirected => System.Console.IsInputRedirected;

    public static bool IsOutputRedirected => System.Console.IsOutputRedirected;

    public static void Init()
    {
        if (_isInitialized) return;

        _realOut = System.Console.Out;

        // NO_COLOR 标准 — https://no-color.org/
        // 检测到 NO_COLOR 环境变量时禁用所有颜色输出
        _noColor = System.Environment.GetEnvironmentVariable("NO_COLOR") is not null;

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows))
        {
            EnableVirtualTerminalProcessing();
        }

        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Console.InputEncoding = System.Text.Encoding.UTF8;
        _isInitialized = true;
    }

    /// <summary>
    /// 是否禁用颜色输出 — 遵循 NO_COLOR 标准
    /// </summary>
    public static bool NoColor => _noColor;

    public static int GetWidth()
    {
        try { return System.Console.WindowWidth; }
        catch
        {
            try { return System.Console.BufferWidth; }
            catch { return 80; }
        }
    }

    public static int GetHeight()
    {
        try { return System.Console.WindowHeight; }
        catch { return 24; }
    }

    public static void WriteLine(string? text = null)
    {
        if (text is null) System.Console.WriteLine();
        else System.Console.WriteLine(text);
        if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
    }

    public static void NewLine()
    {
        System.Console.WriteLine();
        if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
    }

    public static void WriteRaw(string text)
    {
        System.Console.Write(text);
        if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
    }

    public static void WriteRaw(char c)
    {
        System.Console.Write(c);
        if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
    }

    public static void WriteRaw(StringBuilder sb)
    {
        System.Console.Write(sb);
        if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
    }

    public static void WriteRaw(ReadOnlySpan<char> span)
    {
        System.Console.Write(span);
        if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
    }

    public static string ReadLine()
    {
        if (System.Console.IsInputRedirected && !ForceInteractive)
        {
            Diag.WriteLifecycle("[DIAG-TERM] ReadLine: input redirected, ForceInteractive=false, returning empty");
            return string.Empty;
        }
        Diag.WriteLifecycle("[DIAG-TERM] ReadLine: calling Console.ReadLine()...");
        var result = System.Console.ReadLine() ?? string.Empty;
        Diag.WriteLifecycle($"[DIAG-TERM] ReadLine: returned '{(result.Length > 60 ? result[..60] + "..." : result)}'");
        return result;
    }

    public static ConsoleKeyInfo ReadKey(bool intercept = false)
    {
        if (System.Console.IsInputRedirected && !ForceInteractive) return default;
        return System.Console.ReadKey(intercept);
    }

    public static bool KeyAvailable => System.Console.KeyAvailable;

    public static ConsoleColor ForegroundColor
    {
        get => System.Console.ForegroundColor;
        set => System.Console.ForegroundColor = value;
    }

    public static ConsoleColor BackgroundColor
    {
        get => System.Console.BackgroundColor;
        set => System.Console.BackgroundColor = value;
    }

    public static void ResetColor() => System.Console.ResetColor();

    public static IDisposable SetColor(ConsoleColor color) => _noColor ? NoOpDisposable.Instance : new ColorScope(color);

    private sealed class ColorScope : IDisposable
    {
        private readonly ConsoleColor _prev;
        public ColorScope(ConsoleColor color)
        {
            _prev = System.Console.ForegroundColor;
            System.Console.ForegroundColor = color;
        }
        public void Dispose() => System.Console.ForegroundColor = _prev;
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        public void Dispose() { }
    }

    public static void ClearScreen()
    {
        if (!System.Console.IsOutputRedirected)
        {
            System.Console.Clear();
        }
    }

    public static int CursorTop => System.Console.CursorTop;
    public static int CursorLeft => System.Console.CursorLeft;

    public static void SetCursorPosition(int left, int top) => System.Console.SetCursorPosition(left, top);

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

    public static System.IO.TextWriter Out => System.Console.Out;
    public static System.IO.TextReader In => System.Console.In;
    public static System.IO.TextWriter Error => System.Console.Error;

    public static void WriteError(string? text = null)
    {
        if (text is null) System.Console.Error.WriteLine();
        else System.Console.Error.WriteLine(text);
    }

    public static void WriteErrorRaw(string text) => System.Console.Error.Write(text);

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

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int StdOutputHandle = -11;
    private const uint VirtualTerminalProcessingFlag = 0x0004;
    private const uint ProcessedOutputFlag = 0x0001;

    private static void EnableVirtualTerminalProcessing(ILogger? logger = null)
    {
        try
        {
            var handle = GetStdHandle(StdOutputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;

            if (!GetConsoleMode(handle, out var mode)) return;

            if ((mode & VirtualTerminalProcessingFlag) == 0)
            {
                SetConsoleMode(handle, mode | VirtualTerminalProcessingFlag | ProcessedOutputFlag);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "启用虚拟终端处理失败");
        }
    }
}
