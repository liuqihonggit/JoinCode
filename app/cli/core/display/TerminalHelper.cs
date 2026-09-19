namespace JoinCode.Cli;

/// <summary>
/// 终端辅助 — 纯 CLI 模式下的控制台 I/O 封装
/// </summary>
public static class TerminalHelper {
    private static bool _isInitialized;

    /// <summary>
    /// 是否禁用颜色输出 — 由 NO_COLOR 环境变量控制
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

    /// <summary>
    /// ConsoleActor 实例 — 串行化所有 Console I/O，消除后台输出与 ReadLine 竞态（ADR 0100）
    /// </summary>
    private static ConsoleActor? _consoleActor;

    /// <summary>
    /// 获取 ConsoleActor 实例 — 供 ApplicationBuilder 注册 ConsoleActorLoggerProvider
    /// </summary>
    internal static ConsoleActor? GetConsoleActor() => _consoleActor;

    /// <summary>
    /// ConsoleActor 是否激活 — SetOut 重定向后 IsOutputRedirected 会变 true，用此标志修正
    /// </summary>
    private static bool _isActorActive;

    /// <summary>
    /// Init 时捕获的原始 IsOutputRedirected 状态（SetOut 之前）
    /// </summary>
    private static bool _originalIsOutputRedirected;

    /// <summary>
    /// 是否为无头模式 — 未强制交互且（输出重定向或输入重定向）时为 true
    /// </summary>
    public static bool IsHeadless => !ForceInteractive && (IsOutputRedirected || System.Console.IsInputRedirected);

    /// <summary>
    /// 标准输入是否被重定向
    /// </summary>
    public static bool IsInputRedirected => System.Console.IsInputRedirected;

    /// <summary>
    /// 是否输出重定向 — Actor 激活时返回 Init 时捕获的原始状态，避免 SetOut 导致误判
    /// </summary>
    public static bool IsOutputRedirected => _isActorActive ? _originalIsOutputRedirected : System.Console.IsOutputRedirected;

    /// <summary>
    /// 初始化终端 — 捕获真实 stdout、检测 NO_COLOR、Windows 下启用虚拟终端处理、安装 ConsoleActor 串行化 I/O
    /// </summary>
    public static void Init() {
        if (_isInitialized) return;

        _realOut = System.Console.Out;
        _originalIsOutputRedirected = System.Console.IsOutputRedirected;

        // NO_COLOR 标准 — https://no-color.org/
        // 检测到 NO_COLOR 环境变量时禁用所有颜色输出
        _noColor = System.Environment.GetEnvironmentVariable("NO_COLOR") is not null;

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows)) {
            EnableVirtualTerminalProcessing();
        }

        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Console.InputEncoding = System.Text.Encoding.UTF8;

        // 安装 ConsoleActor — 串行化所有 Console I/O，消除后台输出与 ReadLine 竞态（ADR 0100）
        // 仅在非重定向场景启用（E2E 管道场景 In/Out 是不同句柄，无竞态）
        if (!_originalIsOutputRedirected) {
            _consoleActor = new ConsoleActor(_realOut);
            System.Console.SetOut(new ConsoleActorTextWriter(_consoleActor));
            _isActorActive = true;
        }

        _isInitialized = true;
    }

    /// <summary>
    /// 是否禁用颜色输出 — 遵循 NO_COLOR 标准
    /// </summary>
    public static bool NoColor => _noColor;

    /// <summary>
    /// 获取终端宽度（列数）— 优先 WindowWidth，失败回退 BufferWidth，再失败回退 80
    /// </summary>
    /// <returns>终端列数</returns>
    public static int GetWidth() {
        try { return System.Console.WindowWidth; } catch {
            try { return System.Console.BufferWidth; } catch { return 80; }
        }
    }

    /// <summary>
    /// 获取终端高度（行数）— 优先 WindowHeight，失败回退 24
    /// </summary>
    /// <returns>终端行数</returns>
    public static int GetHeight() {
        try { return System.Console.WindowHeight; } catch { return 24; }
    }

    /// <summary>
    /// 写入一行文本并换行 — 经过 ConsoleActor 串行化
    /// </summary>
    /// <param name="text">要写入的文本，null 时仅换行</param>
    public static void WriteLine(string? text = null) {
        if (_consoleActor is not null)
            _consoleActor.WriteLine(text);
        else {
            if (text is null) System.Console.WriteLine();
            else System.Console.WriteLine(text);
            if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
        }
    }

    /// <summary>
    /// 写入空行 — 经过 ConsoleActor 串行化
    /// </summary>
    public static void NewLine() {
        if (_consoleActor is not null)
            _consoleActor.WriteLine();
        else {
            System.Console.WriteLine();
            if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
        }
    }

    /// <summary>
    /// 原始写入字符串（不换行）— 经过 ConsoleActor 串行化
    /// </summary>
    /// <param name="text">要写入的文本</param>
    public static void WriteRaw(string text) {
        if (_consoleActor is not null)
            _consoleActor.WriteRaw(text);
        else {
            System.Console.Write(text);
            if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
        }
    }

    /// <summary>
    /// 原始写入字符（不换行）— 经过 ConsoleActor 串行化
    /// </summary>
    /// <param name="c">要写入的字符</param>
    public static void WriteRaw(char c) {
        if (_consoleActor is not null)
            _consoleActor.WriteRaw(new string(c, 1));
        else {
            System.Console.Write(c);
            if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
        }
    }

    /// <summary>
    /// 原始写入 StringBuilder 内容（不换行）— 经过 ConsoleActor 串行化
    /// </summary>
    /// <param name="sb">要写入的 StringBuilder</param>
    public static void WriteRaw(StringBuilder sb) {
        if (_consoleActor is not null)
            _consoleActor.WriteRaw(sb.ToString());
        else {
            System.Console.Write(sb);
            if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
        }
    }

    /// <summary>
    /// 原始写入字符跨度（不换行）— 经过 ConsoleActor 串行化
    /// </summary>
    /// <param name="span">要写入的字符跨度</param>
    public static void WriteRaw(ReadOnlySpan<char> span) {
        if (_consoleActor is not null)
            _consoleActor.WriteRaw(new string(span));
        else {
            System.Console.Write(span);
            if (System.Console.IsOutputRedirected) System.Console.Out.Flush();
        }
    }

    /// <summary>
    /// 读取一行输入 — 输入重定向且未强制交互时返回空字符串，经过 ConsoleActor 串行化
    /// </summary>
    /// <returns>读取到的行（EOF 时为空字符串）</returns>
    public static string ReadLine() {
        if (System.Console.IsInputRedirected && !ForceInteractive) {
            Diag.WriteLifecycle("[DIAG-TERM] ReadLine: input redirected, ForceInteractive=false, returning empty");
            return string.Empty;
        }
        Diag.WriteLifecycle("[DIAG-TERM] ReadLine: calling Console.ReadLine()...");
        var result = _consoleActor is not null
            ? _consoleActor.ReadLine()
            : (System.Console.ReadLine() ?? string.Empty);
        Diag.WriteLifecycle($"[DIAG-TERM] ReadLine: returned '{(result.Length > 60 ? result[..60] + "..." : result)}'");
        return result;
    }

    /// <summary>
    /// 读取按键 — 输入重定向且未强制交互时返回默认值，经过 ConsoleActor 串行化
    /// </summary>
    /// <param name="intercept">是否拦截按键（不显示到输出）</param>
    /// <returns>按键信息</returns>
    public static ConsoleKeyInfo ReadKey(bool intercept = false) {
        if (System.Console.IsInputRedirected && !ForceInteractive) return default;
        return _consoleActor is not null
            ? _consoleActor.ReadKey(intercept)
            : System.Console.ReadKey(intercept);
    }

    /// <summary>
    /// 读取一行（保留 null EOF 语义）— 经过 ConsoleActor 串行化。
    /// <para>返回 null 表示 EOF（管道关闭），与 Console.ReadLine() 语义一致。</para>
    /// </summary>
    public static string? ReadLineOrNull() {
        if (System.Console.IsInputRedirected && !ForceInteractive) {
            return null;
        }
        return _consoleActor is not null
            ? _consoleActor.ReadLineOrNull()
            : System.Console.ReadLine();
    }

    /// <summary>
    /// 是否有按键可读
    /// </summary>
    public static bool KeyAvailable => System.Console.KeyAvailable;

    /// <summary>
    /// 前景色 — 转发到 Console.ForegroundColor
    /// </summary>
    public static ConsoleColor ForegroundColor {
        get => System.Console.ForegroundColor;
        set => System.Console.ForegroundColor = value;
    }

    /// <summary>
    /// 背景色 — 转发到 Console.BackgroundColor
    /// </summary>
    public static ConsoleColor BackgroundColor {
        get => System.Console.BackgroundColor;
        set => System.Console.BackgroundColor = value;
    }

    /// <summary>
    /// 重置控制台颜色到默认值
    /// </summary>
    public static void ResetColor() => System.Console.ResetColor();

    /// <summary>
    /// 设置前景色并返回作用域 — NO_COLOR 模式返回空操作，离开作用域时自动恢复原色
    /// </summary>
    /// <param name="color">要设置的前景色</param>
    /// <returns>颜色作用域，Dispose 时恢复原色</returns>
    public static IDisposable SetColor(ConsoleColor color) => _noColor ? NoOpDisposable.Instance : new ColorScope(color, _consoleActor);

    /// <summary>
    /// 设置前景色并返回作用域(不通过 ConsoleActor) — 专用于 stderr 错误输出,直接操作 Console.ForegroundColor
    /// </summary>
    public static IDisposable SetColorRaw(ConsoleColor color) => _noColor ? NoOpDisposable.Instance : new RawColorScope(color);

    private sealed class RawColorScope : IDisposable {
        private readonly ConsoleColor _prev;
        private bool _disposed;

        public RawColorScope(ConsoleColor color) {
            _prev = System.Console.ForegroundColor;
            System.Console.ForegroundColor = color;
        }

        public void Dispose() {
            if (_disposed) return; _disposed = true;
            System.Console.ForegroundColor = _prev;
        }
    }

    private sealed class ColorScope : IDisposable {
        private readonly ConsoleColor _prev;
        private readonly ConsoleActor? _actor;
        private bool _disposed;

        public ColorScope(ConsoleColor color, ConsoleActor? actor) {
            _actor = actor;
            if (actor is not null)
                _prev = actor.SetColor(color);
            else {
                _prev = System.Console.ForegroundColor;
                System.Console.ForegroundColor = color;
            }
        }

        public void Dispose() {
            if (_disposed) return; _disposed = true;
            if (_actor is not null)
                _actor.ResetColor();
            else
                System.Console.ForegroundColor = _prev;
        }
    }

    private sealed class NoOpDisposable : IDisposable {
        public static readonly NoOpDisposable Instance = new();
        public void Dispose() { }
    }

    /// <summary>
    /// 清屏 — 输出重定向时不执行
    /// </summary>
    public static void ClearScreen() {
        if (!IsOutputRedirected) {
            if (_consoleActor is not null)
                _consoleActor.ClearScreen();
            else
                System.Console.Clear();
        }
    }

    /// <summary>
    /// 光标行位置（顶部坐标）
    /// </summary>
    public static int CursorTop => System.Console.CursorTop;

    /// <summary>
    /// 光标列位置（左侧坐标）
    /// </summary>
    public static int CursorLeft => System.Console.CursorLeft;

    /// <summary>
    /// 设置光标位置 — 经过 ConsoleActor 串行化
    /// </summary>
    /// <param name="left">列坐标</param>
    /// <param name="top">行坐标</param>
    public static void SetCursorPosition(int left, int top) {
        if (_consoleActor is not null)
            _consoleActor.SetCursorPosition(left, top);
        else
            System.Console.SetCursorPosition(left, top);
    }

    /// <summary>
    /// 重定向标准输出到指定写入器
    /// </summary>
    /// <param name="writer">目标写入器</param>
    public static void SetOut(System.IO.TextWriter writer) => System.Console.SetOut(writer);

    /// <summary>
    /// 输出到真实 stdout（绕过 SetOut 重定向）— 用于交互式提示。
    /// <para>⚠ 防御性检测：当 stdout 被重定向时（E2E 测试/管道场景），此方法绕过重定向导致输出无法被捕获。</para>
    /// <para>若需在非交互模式下输出可被捕获的内容，请改用 <see cref="WriteLine"/>。</para>
    /// </summary>
    public static void WriteLineReal(string? text = null) {
        if (System.Console.IsOutputRedirected && !_isActorActive) {
            System.Console.Error.WriteLine($"[TerminalHelper] 警告: WriteLineReal 在 stdout 重定向时被调用，E2E 测试将捕获不到此输出。请改用 WriteLine。 text={text}");
        }
        if (_consoleActor is not null) {
            _consoleActor.WriteLine(text);
        } else {
            if (text is null) RealOut.WriteLine();
            else RealOut.WriteLine(text);
            RealOut.Flush();
        }
    }

    /// <summary>
    /// 输出到真实 stdout（绕过 SetOut 重定向）— 用于交互式提示。
    /// <para>⚠ 防御性检测：当 stdout 被重定向时（E2E 测试/管道场景），此方法绕过重定向导致输出无法被捕获。</para>
    /// <para>若需在非交互模式下输出可被捕获的内容，请改用 <see cref="WriteLine"/>。</para>
    /// </summary>
    public static void WriteRawReal(string text) {
        if (System.Console.IsOutputRedirected && !_isActorActive) {
            System.Console.Error.WriteLine($"[TerminalHelper] 警告: WriteRawReal 在 stdout 重定向时被调用，E2E 测试将捕获不到此输出。请改用 Write。 text={text}");
        }
        if (_consoleActor is not null) {
            _consoleActor.WriteRaw(text);
        } else {
            RealOut.Write(text);
            RealOut.Flush();
        }
    }

    /// <summary>
    /// 标准输出写入器
    /// </summary>
    public static System.IO.TextWriter Out => System.Console.Out;

    /// <summary>
    /// 标准输入读取器
    /// </summary>
    public static System.IO.TextReader In => System.Console.In;

    /// <summary>
    /// 标准错误写入器
    /// </summary>
    public static System.IO.TextWriter Error => System.Console.Error;

    /// <summary>
    /// 写入一行错误文本并换行
    /// </summary>
    /// <param name="text">要写入的错误文本，null 时仅换行</param>
    public static void WriteError(string? text = null) {
        if (text is null) System.Console.Error.WriteLine();
        else System.Console.Error.WriteLine(text);
    }

    /// <summary>
    /// 原始写入错误流（不换行）
    /// </summary>
    /// <param name="text">要写入的错误文本</param>
    public static void WriteErrorRaw(string text) => System.Console.Error.Write(text);

    /// <summary>
    /// 控制台输出编码
    /// </summary>
    public static System.Text.Encoding OutputEncoding {
        get => System.Console.OutputEncoding;
        set => System.Console.OutputEncoding = value;
    }

    /// <summary>
    /// 取消按键事件（Ctrl+C）— 转发到 Console.CancelKeyPress
    /// </summary>
    public static event ConsoleCancelEventHandler CancelKeyPress {
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

    private static void EnableVirtualTerminalProcessing(ILogger? logger = null) {
        try {
            var handle = GetStdHandle(StdOutputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;

            if (!GetConsoleMode(handle, out var mode)) return;

            if ((mode & VirtualTerminalProcessingFlag) == 0) {
                SetConsoleMode(handle, mode | VirtualTerminalProcessingFlag | ProcessedOutputFlag);
            }
        } catch (Exception ex) {
            logger?.LogWarning(ex, "启用虚拟终端处理失败");
        }
    }
}