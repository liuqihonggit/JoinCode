namespace JoinCode.Tui.Views;

/// <summary>
/// 输入框组件 — 用户输入命令的 TUI 入口。
/// 对齐 claude code 的 PromptInput 组件。
/// </summary>
public sealed class PromptView : ITuiComponent
{
    private readonly CommandQueue _queue;
    private readonly View _container;
    private readonly Label _promptLabel;
    private readonly TextField _textField;
    private readonly CommandHistory _history = new();

    /// <summary>
    /// 创建 PromptView。
    /// </summary>
    /// <param name="queue">命令队列（用户输入入队目标）。</param>
    public PromptView(CommandQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));

        _container = new View
        {
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = true,
        };

        _promptLabel = new Label
        {
            Text = "> ",
            X = 0,
            Y = 0,
        };

        _textField = new TextField
        {
            X = Pos.Right(_promptLabel),
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
        };

        _textField.KeyDown += OnKeyDown;
        _container.Add(_promptLabel, _textField);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>当前输入文本。</summary>
    public string InputText => _textField.Text.ToString();

    /// <summary>输入框焦点。</summary>
    public void SetFocus() => _textField.SetFocus();

    /// <inheritdoc />
    public void OnQueueChanged(QueueSnapshot snapshot)
    {
    }

    /// <inheritdoc />
    public void OnResize(int cols, int rows)
    {
        _container.Width = Dim.Fill();
    }

    private void OnKeyDown(object? sender, TuiKey key)
    {
        if (key == TuiKey.Enter)
        {
            var text = _textField.Text.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                _queue.Enqueue(new QueuedCommand(text, CommandOrigin.User, QueuePriority.Next));
                _history.Add(text);
                _textField.Text = "";
            }
        }
        else if (key == TuiKey.Tab)
        {
            var text = _textField.Text.ToString() ?? string.Empty;
            var completed = TabCompleter.Complete(text);
            if (completed is not null)
                _textField.Text = completed;
        }
        else if (key == TuiKey.CursorUp)
        {
            var prev = _history.NavigateUp();
            if (prev is not null)
                _textField.Text = prev;
        }
        else if (key == TuiKey.CursorDown)
        {
            var next = _history.NavigateDown();
            _textField.Text = next ?? string.Empty;
        }
    }
}
