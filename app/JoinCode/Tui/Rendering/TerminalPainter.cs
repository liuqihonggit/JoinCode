namespace JoinCode.Tui.Rendering;

/// <summary>
/// 终端唯一绘制入口 — 所有 UI 变更必须经此入口，禁止业务代码直接 Console.Write 或操作 Terminal.Gui Application。
/// 线程安全：非 MainLoop 线程通过 Invoke 投递到 MainLoop，由 Application 保证单线程渲染。
/// 对齐 claude code 的 writeDiffToTerminal 唯一 stdout 写入点设计。
/// </summary>
public sealed class TerminalPainter
{
    private readonly Action<Action> _invoke;
    private readonly List<ITuiComponent> _components = new();
    private readonly object _lock = new();

    /// <summary>
    /// 创建 TerminalPainter。
    /// </summary>
    /// <param name="invoke">MainLoop 投递函数（将绘制操作投递到 Terminal.Gui 主循环）。</param>
    public TerminalPainter(Action<Action> invoke)
    {
        _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
    }

    /// <summary>投递同步渲染请求到 MainLoop（线程安全）。</summary>
    /// <param name="drawAction">绘制操作。</param>
    public void Invoke(Action drawAction) => _invoke(drawAction);

    /// <summary>注册 TUI 组件到渲染树。</summary>
    /// <param name="component">TUI 组件。</param>
    public void Register(ITuiComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        lock (_lock)
        {
            _components.Add(component);
        }
    }

    /// <summary>注销 TUI 组件。</summary>
    /// <param name="component">TUI 组件。</param>
    public void Unregister(ITuiComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        lock (_lock)
        {
            _components.Remove(component);
        }
    }

    /// <summary>获取所有已注册组件的只读快照。</summary>
    public IReadOnlyList<ITuiComponent> GetComponents()
    {
        lock (_lock)
        {
            return _components.ToArray();
        }
    }

    /// <summary>通知所有组件队列状态变化。</summary>
    /// <param name="snapshot">队列快照。</param>
    public void NotifyQueueChanged(QueueSnapshot snapshot)
    {
        IReadOnlyList<ITuiComponent> components;
        lock (_lock)
        {
            components = _components.ToArray();
        }
        _invoke(() =>
        {
            foreach (var c in components)
                c.OnQueueChanged(snapshot);
        });
    }

    /// <summary>通知所有组件终端尺寸变化。</summary>
    /// <param name="cols">列数。</param>
    /// <param name="rows">行数。</param>
    public void NotifyResize(int cols, int rows)
    {
        IReadOnlyList<ITuiComponent> components;
        lock (_lock)
        {
            components = _components.ToArray();
        }
        _invoke(() =>
        {
            foreach (var c in components)
                c.OnResize(cols, rows);
        });
    }
}
