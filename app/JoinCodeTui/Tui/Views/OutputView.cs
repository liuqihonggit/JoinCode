namespace JoinCode.Tui.Views;

/// <summary>
/// 输出流组件 — 显示 Agent 输出和系统消息。
/// 用官方 ListView Suspend/ResumeCollectionChangedEvent API + 内置 ConcurrentQueue：
/// AppendLine 只入队（线程安全，后台线程直接调用），Flush 在 MainLoop 线程批量追加（Suspend → Add → Resume → SetNeedsDraw 一次）。
/// 从 O(N²) 重绘降到 O(N) + 每轮只 SetNeedsDraw 一次。
/// </summary>
public sealed class OutputView : ITuiComponent
{
    private readonly View _container;
    private readonly ListView _listView;
    private readonly ObservableCollection<string> _items = new();
    private readonly ConcurrentQueue<string> _pending = new();
    private long _lastFlushTicks;
    private const int MaxLines = 10000;
    private const int FlushIntervalMs = 50;

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
        _listView.SetSource(_items);

        _container.Add(_listView);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>追加输出行（线程安全，后台线程可直接调用，只入队不触 UI）。</summary>
    /// <param name="line">输出文本行。</param>
    public void AppendLine(string line)
    {
        _pending.Enqueue(line);
    }

    /// <summary>追加多行输出（线程安全）。</summary>
    /// <param name="text">多行文本。</param>
    public void AppendText(string text)
    {
        var parts = text.Split('\n');
        foreach (var part in parts)
        {
            var trimmed = part.TrimEnd('\r');
            _pending.Enqueue(trimmed);
        }
    }

    /// <summary>清空输出（在 MainLoop 线程调用）。</summary>
    public void Clear()
    {
        while (_pending.TryDequeue(out _)) { }
        _listView.SuspendCollectionChangedEvent();
        _items.Clear();
        _listView.ResumeSuspendCollectionChangedEvent();
        _listView.SetNeedsDraw();
        _lastFlushTicks = Environment.TickCount64;
    }

    /// <summary>
    /// 批量刷新 — 在 MainLoop.Iteration 回调中调用。
    /// Suspend → 批量 Add → Resume → SetNeedsDraw 一次。节流 50ms。
    /// </summary>
    public void Flush()
    {
        if (_pending.IsEmpty) return;
        var now = Environment.TickCount64;
        if (now - _lastFlushTicks < FlushIntervalMs) return;

        _listView.SuspendCollectionChangedEvent();
        while (_pending.TryDequeue(out var line))
        {
            _items.Add(line);
            while (_items.Count > MaxLines)
                _items.RemoveAt(0);
        }
        _listView.ResumeSuspendCollectionChangedEvent();
        _listView.SetNeedsDraw();
        _lastFlushTicks = now;
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
