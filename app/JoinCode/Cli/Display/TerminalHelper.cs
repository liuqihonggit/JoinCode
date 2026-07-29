namespace JoinCode.Cli;

/// <summary>
/// 终端辅助 — 纯 CLI 模式下的控制台 I/O 封装
/// </summary>
public static class TerminalHelper
{
    private static bool _isInitialized;

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

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows))
        {
            EnableVirtualTerminalProcessing();
        }

        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Console.InputEncoding = System.Text.Encoding.UTF8;
        _isInitialized = true;
    }

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
        if (System.Console.IsInputRedirected && !ForceInteractive) return string.Empty;
        return System.Console.ReadLine() ?? string.Empty;
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

    private static void EnableVirtualTerminalProcessing()
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
            System.Diagnostics.Trace.WriteLine($"启用虚拟终端处理失败: {ex.Message}");
        }
    }
}
