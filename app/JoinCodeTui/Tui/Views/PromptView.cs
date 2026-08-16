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
            CanFocus = false,
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
                _textField.Text = "";
            }
        }
    }
}
