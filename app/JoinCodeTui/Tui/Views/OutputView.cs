namespace JoinCode.Tui.Views;

/// <summary>
/// 输出流组件 — 显示 Agent 输出和系统消息。
/// 对齐 claude code 的 Output 组件，自动滚动到底部。
/// </summary>
public sealed class OutputView : ITuiComponent
{
    private readonly View _container;
    private readonly Label _textLabel;
    private readonly List<string> _lines = new();
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

        _textLabel = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Text = "",
        };

        _container.Add(_textLabel);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>追加输出行（线程安全由 TerminalPainter.Invoke 保证）。</summary>
    /// <param name="line">输出文本行。</param>
    public void AppendLine(string line)
    {
        _lines.Add(line);
        if (_lines.Count > MaxLines)
        {
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        }
        RefreshDisplay();
    }

    /// <summary>追加多行输出。</summary>
    /// <param name="text">多行文本。</param>
    public void AppendText(string text)
    {
        var parts = text.Split('\n');
        foreach (var part in parts)
        {
            var trimmed = part.TrimEnd('\r');
            _lines.Add(trimmed);
        }
        if (_lines.Count > MaxLines)
        {
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        }
        RefreshDisplay();
    }

    /// <summary>清空输出。</summary>
    public void Clear()
    {
        _lines.Clear();
        RefreshDisplay();
    }

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

    private void RefreshDisplay()
    {
        _textLabel.Text = string.Join(Environment.NewLine, _lines);
    }
}
