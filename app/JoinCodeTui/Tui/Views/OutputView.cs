namespace JoinCode.Tui.Views;

/// <summary>
/// 输出流组件 — 显示 Agent 输出和系统消息。
/// 对齐 claude code 的 Output 组件，用G ListView + ObservableCollection 实现滚动，
/// 替代 Label 全量重绘，支持大量输出流畅滚动。
/// </summary>
public sealed class OutputView : ITuiComponent
{
    private readonly View _container;
    private readonly ListView _listView;
    private readonly ObservableCollection<string> _items = new();
    private const int MaxLines = 10000;

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

    /// <summary>追加输出行（线程安全由 TerminalPainter.Invoke 保证）。</summary>
    /// <param name="line">输出文本行。</param>
    public void AppendLine(string line)
    {
        _items.Add(line);
        if (_items.Count > MaxLines)
        {
            _items.RemoveAt(0);
        }
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
        {
            _items.RemoveAt(0);
        }
    }

    /// <summary>清空输出。</summary>
    public void Clear()
    {
        _items.Clear();
    }

    /// <summary>获取当前所有输出行的只读快照。</summary>
    /// <returns>输出行列表。</returns>
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
