namespace NativeTui.Core.Terminal;

/// <summary>
/// 崩溃/退出路径终端恢复兜底 — CursorLifecycle 注释契约兑现（"宿主全局异常兜底统一补发 Show"）：
/// 注册 ProcessExit / UnhandledException / CancelKeyPress 三钩子，进程终止前同步发射
/// 硬件光标 Show + bracketed paste Disable（幂等可重入 — 多钩子触达无害；
/// 写失败静默吞掉 — 兜底不因输出管道已死而二次抛出）。
/// 对齐基准 ink.tsx:1472-1505 signal-exit 同步 writeSync 恢复链：非主线程未处理异常 /
/// 信号退出等 raw mode using 无法覆盖的路径，终端不残留"光标隐藏 + paste 泄漏"状态。
/// Dispose 解除钩子注册（正常退出由 WriteExit 对称恢复，guard 不重复发射）。
/// </summary>
public sealed class TerminalRestoreGuard : IDisposable
{
    private readonly TextWriter _output;
    private bool _disposed;

    /// <summary>注册崩溃/退出兜底钩子（raw mode 进入后立即挂载 — 覆盖随后全部运行期）。</summary>
    public TerminalRestoreGuard(TextWriter output)
    {
        _output = output;
        AppDomain.CurrentDomain.ProcessExit += RestoreTerminal;
        AppDomain.CurrentDomain.UnhandledException += RestoreTerminal;
        Console.CancelKeyPress += RestoreTerminal;
    }

    /// <summary>发射恢复序列（钩子触发入口 — 幂等；测试注入面）。</summary>
    internal void RestoreTerminalForTest() => RestoreTerminal(null, EventArgs.Empty);

    private void RestoreTerminal(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _output.Write(CursorLifecycle.Show + DecPrivateMode.Disable(DecPrivateModeKind.BracketedPaste));
            _output.Flush();
        }
        catch (Exception)
        {
            // 兜底静默 — 恢复失败不再抛（崩溃场景输出管道可能已失效）
        }
    }

    /// <summary>解除钩子注册（正常退出路径由 WriteExit 对称恢复 — 兜底解除后不再发射）。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= RestoreTerminal;
        AppDomain.CurrentDomain.UnhandledException -= RestoreTerminal;
        Console.CancelKeyPress -= RestoreTerminal;
    }
}
