namespace JoinCode.Tui.Rendering;

/// <summary>
/// 批量输出缓冲区 — 后台线程入队 ConcurrentQueue（不阻塞），
/// MainLoop.Iteration 回调中批量追加到 OutputView（无需 Invoke）。
/// 完全消除同步 painter.Invoke 阻塞 MainLoop 的问题。
/// </summary>
public sealed class OutputBatcher
{
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly OutputView _outputView;

    /// <summary>
    /// 创建 OutputBatcher。
    /// </summary>
    public OutputBatcher(OutputView outputView)
    {
        _outputView = outputView;
    }

    /// <summary>
    /// 入队一行输出（非阻塞，后台线程安全）。
    /// </summary>
    public void Enqueue(string line)
    {
        _pending.Enqueue(line);
    }

    /// <summary>
    /// 在 MainLoop.Iteration 中调用 — 直接追加待处理行到 OutputView（已在 MainLoop 线程，无需 Invoke）。
    /// </summary>
    public void DrainOnIteration()
    {
        if (_pending.IsEmpty) return;
        while (_pending.TryDequeue(out var line))
            _outputView.AppendLine(line);
        _outputView.Flush();
    }

    /// <summary>
    /// 立即刷新（通过 painter.Invoke 同步等待）。用于非 Iteration 上下文。
    /// </summary>
    public void FlushNow(TerminalPainter painter)
    {
        if (_pending.IsEmpty) return;
        painter.Invoke(DrainOnIteration);
    }
}
