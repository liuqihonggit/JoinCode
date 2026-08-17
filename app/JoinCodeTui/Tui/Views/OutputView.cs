namespace JoinCode.Tui.Views;

/// <summary>
/// 可抑制通知的 ObservableCollection — SuppressNotifications=true 时 Add/Clear 不触发 CollectionChanged，
/// 避免 ListView 每行重绘。Flush 时设 false 并触发 Reset 通知一次性刷新。
/// </summary>
internal sealed class SuppressibleObservableCollection<T> : System.Collections.ObjectModel.ObservableCollection<T>
{
    public bool SuppressNotifications { get; set; }

    protected override void OnCollectionChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (!SuppressNotifications)
            base.OnCollectionChanged(e);
    }

    /// <summary>
    /// 手动触发 Reset 通知 — 用于 Flush 时一次性通知 ListView 重绘。
    /// </summary>
    public void NotifyReset()
    {
        SuppressNotifications = false;
        base.OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        SuppressNotifications = true;
    }
}

/// <summary>
/// 输出流组件 — 显示 Agent 输出和系统消息。
/// 用 ListView + SuppressibleObservableCollection + 节流刷新：
/// AppendLine 抑制 CollectionChanged 不触发重绘，Flush 时一次性 Reset 通知，
/// 每 100ms 最多刷新一次，从 O(N²) 降到 O(N)。
/// </summary>
public sealed class OutputView : ITuiComponent
{
    private readonly View _container;
    private readonly ListView _listView;
    private readonly SuppressibleObservableCollection<string> _items = new();
    private bool _dirty;
    private long _lastFlushTicks;
    private const int MaxLines = 10000;
    private const int FlushIntervalMs = 100;

    /// <summary>
    /// 创建 OutputView。
    /// </summary>
    /// <param name="height">初始高度（行数），0 表示自动填充。</param>
    public OutputView(int height = 0)
    {
        _container = new View
        {
            Width = Dim.Fill(),
            Height = height > 0 ? height : Dim.Fill(),
        };

        _listView = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _items.SuppressNotifications = true;
        _listView.SetSource(_items);

        _container.Add(_listView);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>追加输出行（线程安全由 TerminalPainter.Invoke 保证）。</summary>
    /// <param name="line">输出文本行。</param>
    public void AppendLine(string line)
    {
        _items.Add(line);
        if (_items.Count > MaxLines)
            _items.RemoveAt(0);
        _dirty = true;
        TryAutoFlush();
    }

    /// <summary>追加多行输出。</summary>
    /// <param name="text">多行文本。</param>
    public void AppendText(string text)
    {
        var parts = text.Split('\n');
        foreach (var part in parts)
        {
            var trimmed = part.TrimEnd('\r');
            _items.Add(trimmed);
        }
        while (_items.Count > MaxLines)
            _items.RemoveAt(0);
        _dirty = true;
        TryAutoFlush();
    }

    /// <summary>清空输出。</summary>
    public void Clear()
    {
        _items.Clear();
        _dirty = true;
        Flush();
    }

    /// <summary>
    /// 如果距上次刷新超过 FlushIntervalMs，立即刷新 ListView。
    /// </summary>
    public void TryAutoFlush()
    {
        var now = Environment.TickCount64;
        if (now - _lastFlushTicks < FlushIntervalMs)
            return;
        Flush();
    }

    /// <summary>
    /// 强制刷新 ListView — 恢复通知并触发 Reset 事件一次性重绘。
    /// </summary>
    public void Flush()
    {
        if (!_dirty) return;
        _items.NotifyReset();
        _dirty = false;
        _lastFlushTicks = Environment.TickCount64;
    }

    /// <summary>获取当前所有输出行的只读快照。</summary>
    public IReadOnlyList<string> GetLines() => _items.ToArray();

    /// <inheritdoc />
    public void OnQueueChanged(QueueSnapshot snapshot)
    {
    }

    /// <inheritdoc />
    public void OnResize(int cols, int rows)
    {
        _container.Width = Dim.Fill();
        _container.Height = Dim.Fill();
    }
}
