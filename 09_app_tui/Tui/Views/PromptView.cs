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
    private IReadOnlyList<string> _slashCommands = [];
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
            WordWrap = false,
            ViewportSettings = ViewportSettingsFlags.HasScrollBars,
            GutterOptions = GutterOptions.None,
        };

        // Ctrl+Enter 绑定到 Command.Accept（发送），不干扰 Editor 默认命令路由
        _editor.KeyBindings.Add(TuiKey.Enter.WithCtrl, Command.Accept);
        _editor.Accepted += OnAccepted;
        // Tab 和历史导航用 KeyDown 事件（只拦截特定键，不影响 Backspace 等默认键）
        _editor.KeyDown += OnKeyDown;

        _container.Add(_promptLabel, _editor);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>当前输入文本。</summary>
    public string InputText => _editor.Text ?? string.Empty;

    /// <summary>设置斜杠命令列表（用于 Tab 补全，从底层 ISlashCommandCatalog 获取）。</summary>
    public void SetSlashCommands(IReadOnlyList<string> commands) => _slashCommands = commands ?? [];

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

    private void OnAccepted(object? sender, CommandEventArgs e)
    {
        var text = _editor.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            _queue.Enqueue(new QueuedCommand(text, CommandOrigin.User, QueuePriority.Next));
            _history.Add(text);
            _editor.Text = "";
        }
    }

    private void OnKeyDown(object? sender, TuiKey key)
    {
        // Tab 补全
        if (key == TuiKey.Tab)
        {
            var text = _editor.Text ?? string.Empty;
            var completed = TabCompleter.Complete(text, _slashCommands);
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
