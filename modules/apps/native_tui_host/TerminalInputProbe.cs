namespace NativeTui.Host;

/// <summary>
/// 双终端验收的输入取证入口（TASK129）。复用生产 raw 模式、stdin 输入泵与窗口尺寸源，
/// 不启动引擎、不提交回合。只记录按键形态，不回显剪贴板内容。
/// </summary>
internal static class TerminalInputProbe {
    private const int PollMilliseconds = 50;
    private const int SampleWindowMilliseconds = 180_000;
    private static readonly DecPrivateModeKind[] MouseModes = [
        DecPrivateModeKind.MouseClickTracking,
        DecPrivateModeKind.MouseEventTracking,
        DecPrivateModeKind.MouseAnyEventTracking,
        DecPrivateModeKind.MouseSgrEncoding,
    ];

    /// <summary>在真实 Windows 终端采样；Ctrl+Q 结束，超时返回非零，不自动判定任何矩阵项。</summary>
    internal static async Task<int> RunAsync() {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || Console.IsOutputRedirected) {
            Console.Error.WriteLine("--probe-terminal-input 需要 Windows 交互终端，且不能重定向 stdin/stdout。请在 Windows Terminal 或 Console Host 中直接启动；管道验证请用 --selftest-input。");
            return 1;
        }

        var originalEncoding = Console.OutputEncoding;
        Console.OutputEncoding = Encoding.UTF8;
        try {
            return await SampleAsync(Console.Out).ConfigureAwait(false);
        } finally {
            Console.OutputEncoding = originalEncoding;
        }
    }

    /// <summary>采样并对称还原本次启用的终端模式。</summary>
    private static async Task<int> SampleAsync(TextWriter output) {
        using var raw = new RawConsoleMode();
        using var restore = new TerminalRestoreGuard(output);
        using var pump = new RawStdinPump();
        var pollSize = new ConsoleTerminalSizeSource();
        var size = pollSize.Current;
        var mouseTracking = JccFeatureFlags.MouseTrackingEnabled;
        var enables = mouseTracking ? string.Concat(MouseModes.Select(DecPrivateMode.Enable)) : string.Empty;
        var disables = mouseTracking ? string.Concat(MouseModes.Reverse().Select(DecPrivateMode.Disable)) : string.Empty;
        var deadline = Environment.TickCount64 + SampleWindowMilliseconds;
        var finished = false;
        var index = 0;

        try {
            output.Write(DecPrivateMode.Enable(DecPrivateModeKind.BracketedPaste) + enables);
            var wtHost = TerminalHostDetector.IsWindowsTerminalHosting();
            var wtSession = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION"));
            output.WriteLine($"[terminal-input] wt-host={wtHost} wt-session={wtSession} mouse={mouseTracking} cols={size.Cols} rows={size.Rows}");
            output.WriteLine("请仅输入测试字符并粘贴测试文本。Ctrl+Q 结束；3 分钟后自动退出。输出仅为采样，不代表矩阵通过。");
            output.Flush();

            while (!finished && pump.IsReaderAlive && Environment.TickCount64 < deadline) {
                var current = pollSize.Current;
                if (current != size) {
                    size = current;
                    output.WriteLine($"[resize] cols={size.Cols} rows={size.Rows}");
                }

                pump.Drain(RecordKey);
                if (!finished) {
                    await Task.Delay(PollMilliseconds).ConfigureAwait(false);
                }
            }

            if (finished) {
                output.WriteLine($"[terminal-input] 采样结束，已记录 {index} 个事件；请按矩阵核对。");
                return 0;
            }

            Console.Error.WriteLine($"[terminal-input] 采样未完成（读线程已退出或窗口到期），已记录 {index} 个事件。请重启本探针继续未测项。");
            return 1;
        } finally {
            output.Write(disables + DecPrivateMode.Disable(DecPrivateModeKind.BracketedPaste));
            output.Flush();
        }

        // 记下非悬停事件，避免鼠标移动刷屏掩盖键位证据。
        void RecordKey(KeyPress key) {
            if (finished || key.Mouse is { Kind: MouseKind.Move }) {
                return;
            }

            output.WriteLine($"[{++index}] {DescribeKey(key)}");
            finished = key is { Kind: KeyKind.Char, Ctrl: true, Meta: false, Shift: false, Input: "q" };
        }
    }

    /// <summary>以安全的诊断文本描述按键，不把未经转义的输入直接写回终端。</summary>
    internal static string DescribeKey(KeyPress key) {
        if (key.Mouse is { } mouse) {
            return $"{key.Kind} {mouse.Kind} button={mouse.Button} col={mouse.Col} row={mouse.Row} ctrl={mouse.Ctrl} alt={mouse.Meta} shift={mouse.Shift}";
        }

        var firstScalar = key is { Kind: KeyKind.Char, Input.Length: > 0 } && Rune.TryGetRuneAt(key.Input, 0, out var rune)
            ? $" first=U+{rune.Value:X4}"
            : string.Empty;
        return $"{key.Kind} ctrl={key.Ctrl} alt={key.Meta} shift={key.Shift} chars={key.Input.Length}{firstScalar}";
    }
}
