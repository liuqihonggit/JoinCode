namespace NativeTui.Core.Repl;

/// <summary>
/// Win32 ReadConsoleInput 输入泵（TASK077）— 后台线程阻塞读 <see cref="Win32ConsoleInput.ReadEvents"/>
/// → KEY_EVENT 经 <see cref="ConsoleKeyAdapter"/> 归一 KeyPress / MOUSE_EVENT 包 KeyPress(Mouse) →
/// 无界通道缓冲 → 主循环帧拍 <see cref="Drain"/> 非阻塞排空。
/// 替代 <see cref="ReadKeyPump"/>（Console.ReadKey 跳过 MOUSE_EVENT）+ <see cref="RawStdinPump"/>
/// （stdin ReadFile 同跳过）— ReadConsoleInput P/Invoke 直读 INPUT_RECORD，键盘鼠标同通道。
/// 仅 Windows（Linux 走 RawStdinPump VT 序列天然含鼠标）。线程模型对齐 RawStdinPump：
/// IsBackground 后台线程，Dispose 不等线程终结（ReadConsoleInput 阻塞由进程退出回收）。
/// </summary>
public sealed class Win32ConsoleInputPump : IInputPump, IDisposable
{
    private readonly Channel<KeyPress> _keys = Channel.CreateUnbounded<KeyPress>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _reader;
    private readonly Terminal.Win32ConsoleInput _input;
    private bool _disposed;

    /// <summary>创建并启动读线程（构造即开始消费 console input — 生命周期由 Dispose/进程退出终结）。</summary>
    public Win32ConsoleInputPump()
    {
        _input = new Terminal.Win32ConsoleInput();
        _reader = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "win32-console-input-pump",
        };
        _reader.Start();
    }

    /// <summary>非阻塞排空已解析键（帧拍消费 — 通道无键立即返回 false）。</summary>
    public bool Drain(Action<KeyPress> dispatch)
    {
        var dispatched = false;
        while (_keys.Reader.TryRead(out var key))
        {
            dispatch(key);
            dispatched = true;
        }

        return dispatched;
    }

    /// <summary>停泵（取消读循环 + 通道完成 + 恢复 console mode — 阻塞中的 ReadConsoleInput 由进程退出回收）。</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _cts.Cancel();
            _keys.Writer.TryComplete();
            _input.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// 读循环（后台线程）— ReadConsoleInput 阻塞读批次 → 逐事件转 KeyPress → 通道投递。
    /// ReadConsoleInput 失败（stdin 非 console handle / Dispose 竞态）返回空批次，循环空转；
    /// 通道已关闭（Dispose 后）TryWrite 返 false 静默退出。
    /// </summary>
    private void ReadLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var events = _input.ReadEvents();
                foreach (var evt in events)
                {
                    if (ConvertEvent(evt) is { } key && !_keys.Writer.TryWrite(key))
                    {
                        return;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
        {
            // Dispose 竞态 — 静默退出
        }
    }

    /// <summary>转换单个 InputEvent 为 KeyPress（null = 无可识别事件）。
    /// KEY_EVENT → ConsoleKeyAdapter.ToKeyPress 归一；MOUSE_EVENT → KeyPress(Mouse) 包装。
    /// 提取为 internal 可单元测试（转换逻辑独立于 ReadConsoleInput 阻塞读）。</summary>
    internal static KeyPress? ConvertEvent(Terminal.Win32ConsoleInput.InputEvent evt)
    {
        if (evt.Key is { } keyInfo)
        {
            return ConsoleKeyAdapter.ToKeyPress(keyInfo);
        }

        if (evt.Mouse is { } mouse)
        {
            return new KeyPress(KeyKind.Mouse, string.Empty,
                Ctrl: mouse.Ctrl, Meta: mouse.Meta, Shift: mouse.Shift, Mouse: mouse);
        }

        return null;
    }
}
