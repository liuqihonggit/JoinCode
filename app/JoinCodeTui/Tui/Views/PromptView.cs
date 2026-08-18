namespace JoinCode.Tui.Views;

/// <summary>
/// 输入框组件 — 用户输入命令的 TUI 入口。
/// 多行 Editor 输入：Ctrl+Enter 发送，Enter 换行，Tab 补全，Ctrl+Up/Down 历史导航。
/// </summary>
public sealed class PromptView : ITuiComponent
{
    private readonly CommandQueue _queue;
    private readonly View _container;
    private readonly Label _promptLabel;
    private readonly Editor _editor;
    private readonly CommandHistory _history = new();
    private const int InputHeight = 3;

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
            Height = InputHeight,
            CanFocus = true,
        };

        _promptLabel = new Label
        {
            Text = "> ",
            X = 0,
            Y = 0,
            Height = 1,
        };

        _editor = new Editor
        {
            X = Pos.Right(_promptLabel),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = false,
            Multiline = true,
            WordWrap = true,
            ViewportSettings = ViewportSettingsFlags.None,
            GutterOptions = GutterOptions.None,
        };

        _editor.KeyDown += OnKeyDown;
        _container.Add(_promptLabel, _editor);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>当前输入文本。</summary>
    public string InputText => _editor.Text ?? string.Empty;

    /// <summary>输入框焦点。</summary>
    public void SetFocus() => _editor.SetFocus();

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
        // Ctrl+Enter 发送
        if (key == TuiKey.Enter.WithCtrl)
        {
            var text = _editor.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                _queue.Enqueue(new QueuedCommand(text, CommandOrigin.User, QueuePriority.Next));
                _history.Add(text);
                _editor.Text = "";
            }
            key.Handled = true;
        }
        // Tab 补全
        else if (key == TuiKey.Tab)
        {
            var text = _editor.Text ?? string.Empty;
            var completed = TabCompleter.Complete(text);
            if (completed is not null)
            {
                _editor.Text = completed;
            }
            key.Handled = true;
        }
        // Ctrl+Up 历史导航
        else if (key == TuiKey.CursorUp.WithCtrl)
        {
            var prev = _history.NavigateUp();
            if (prev is not null)
            {
                _editor.Text = prev;
            }
            key.Handled = true;
        }
        // Ctrl+Down 历史导航
        else if (key == TuiKey.CursorDown.WithCtrl)
        {
            var next = _history.NavigateDown();
            _editor.Text = next ?? string.Empty;
            key.Handled = true;
        }
    }
}
