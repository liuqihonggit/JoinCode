namespace JoinCode.Tui.Views;

/// <summary>
/// 输出流组件 — 显示 Agent 输出和系统消息。
/// 用 Terminal.Gui.Editor.Editor（rope-backed 文本编辑器）：
///   - 支持文本选择（Shift+方向键/鼠标拖拽）和复制（Ctrl+C）
///   - ReadOnly = true 阻止编辑但保留导航和选择
///   - WordWrap = true 在屏幕宽度处软换行，流式 chunk 连续追加
/// 行级环形缓冲区（默认 2048 行，可配置）保证内存不无限增长。
/// </summary>
public sealed class OutputView : ITuiComponent
{
    private readonly View _container;
    private readonly Editor _editor;
    private readonly ConcurrentQueue<string> _pending = new();

    // 行级环形缓冲区
    private readonly string?[] _lines;
    private readonly int _capacity;
    private int _head;  // 最旧行的索引
    private int _count; // 有效行数
    private readonly StringBuilder _currentLine = new();  // 当前正在构建的行（流式 chunk 追加到这里，遇到 \n 才提交）

    private long _lastFlushTicks;
    private const int FlushIntervalMs = 100;

    /// <summary>
    /// 创建 OutputView。
    /// </summary>
    /// <param name="height">初始高度（行数），0 表示自动填充。</param>
    /// <param name="maxLines">最大保留行数（环形缓冲区容量），超出淘汰最旧行。默认 2048。</param>
    public OutputView(int height = 0, int maxLines = 2048)
    {
        _capacity = maxLines;
        _lines = new string?[maxLines];

        _container = new View
        {
            Width = Dim.Fill(),
            Height = height > 0 ? height : Dim.Fill(),
        };

        _editor = new Editor
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            ViewportSettings = ViewportSettingsFlags.HasScrollBars,
            GutterOptions = GutterOptions.None,
        };

        _container.Add(_editor);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>追加输出行（线程安全，后台线程可直接调用，只入队不触 UI）。</summary>
    /// <param name="line">输出文本行（自动追加换行符）。</param>
    public void AppendLine(string line)
    {
        _pending.Enqueue(line + "\n");
    }

    /// <summary>追加流式文本（线程安全，不加换行，连续追加到当前行）。</summary>
    /// <param name="text">流式文本片段。</param>
    public void AppendText(string text)
    {
        _pending.Enqueue(text);
    }

    /// <summary>清空输出（在 MainLoop 线程调用）。</summary>
    public void Clear()
    {
        while (_pending.TryDequeue(out _)) { }
        Array.Clear(_lines);
        _head = 0;
        _count = 0;
        _currentLine.Clear();
        _editor.Text = string.Empty;
        _lastFlushTicks = Environment.TickCount64;
    }

    /// <summary>
    /// 批量刷新 — 在 MainLoop.Iteration 回调中调用。
    /// 从队列取出文本，按 \n 分割提交到环形缓冲，设 Editor.Text，滚动到底部。节流 100ms。
    /// </summary>
    public void Flush()
    {
        if (_pending.IsEmpty) return;
        var now = Environment.TickCount64;
        if (now - _lastFlushTicks < FlushIntervalMs) return;

        var drained = false;
        while (_pending.TryDequeue(out var chunk))
        {
            drained = true;
            AppendToBuffer(chunk);
        }

        if (drained)
        {
            _editor.Text = BuildText();
            if (_editor.Document is not null)
            {
                _editor.CaretOffset = _editor.Document.TextLength;
            }
        }

        _lastFlushTicks = now;
    }

    private void AppendToBuffer(string chunk)
    {
        var span = chunk.AsSpan();
        var start = 0;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '\n')
            {
                _currentLine.Append(span.Slice(start, i - start));
                CommitCurrentLine();
                start = i + 1;
            }
        }

        if (start < span.Length)
        {
            _currentLine.Append(span.Slice(start));
        }
    }

    private void CommitCurrentLine()
    {
        _lines[(_head + _count) % _capacity] = _currentLine.ToString();
        if (_count < _capacity)
        {
            _count++;
        }
        else
        {
            _head = (_head + 1) % _capacity;
        }
        _currentLine.Clear();
    }

    private string BuildText()
    {
        if (_count == 0 && _currentLine.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < _count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(_lines[(_head + i) % _capacity]);
        }

        if (_currentLine.Length > 0)
        {
            if (_count > 0) sb.Append('\n');
            sb.Append(_currentLine);
        }

        return sb.ToString();
    }

    /// <summary>获取当前所有输出行的只读快照。</summary>
    public IReadOnlyList<string> GetLines()
    {
        var result = new List<string>(_count + 1);
        for (var i = 0; i < _count; i++)
        {
            result.Add(_lines[(_head + i) % _capacity] ?? string.Empty);
        }
        if (_currentLine.Length > 0)
        {
            result.Add(_currentLine.ToString());
        }
        return result;
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
}
