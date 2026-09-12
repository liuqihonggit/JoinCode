namespace JoinCode.Tui.Views;

/// <summary>
/// 工具栏操作类型 — 对应 F1-F5 快捷键。
/// </summary>
public enum ToolBarAction
{
    /// <summary>新建会话（F1）</summary>
    [EnumValue("new")] New,
    /// <summary>暂停/恢复（F2）</summary>
    [EnumValue("pause")] Pause,
    /// <summary>停止当前任务（F3）</summary>
    [EnumValue("stop")] Stop,
    /// <summary>切换 Chat 视图（F4）</summary>
    [EnumValue("chat")] Chat,
    /// <summary>打开统计面板（F5）</summary>
    [EnumValue("stats")] Stats,
}

/// <summary>
/// 工具栏组件 — 提供会话控制按钮（New/Pause/Stop/Chat/Stats）。
/// 按钮用 Pos.Right 链式水平排列（相对定位），支持 F1-F5 快捷键。
/// 对齐 Agent TUI 设计规范的工具栏区域。
/// </summary>
public sealed class ToolBarView : ITuiComponent
{
    private readonly View _container;
    private readonly Button _newButton;
    private readonly Button _pauseButton;
    private readonly Button _stopButton;
    private readonly Button _chatButton;
    private readonly Button _statsButton;

    /// <summary>
    /// 创建 ToolBarView。
    /// </summary>
    public ToolBarView()
    {
        _container = new View
        {
            Width = Dim.Fill(),
            Height = 1,
        };

        _newButton = new Button { Text = "▶ New (F1)", X = 0, Y = 0 };
        _pauseButton = new Button { Text = "⏸ Pause (F2)", X = Pos.Right(_newButton) + 1, Y = 0 };
        _stopButton = new Button { Text = "⏹ Stop (F3)", X = Pos.Right(_pauseButton) + 1, Y = 0 };
        _chatButton = new Button { Text = "💬 Chat (F4)", X = Pos.Right(_stopButton) + 1, Y = 0 };
        _statsButton = new Button { Text = "📊 Stats (F5)", X = Pos.Right(_chatButton) + 1, Y = 0 };

        _newButton.Accepting += (_, _) => TriggerAction(ToolBarAction.New);
        _pauseButton.Accepting += (_, _) => TriggerAction(ToolBarAction.Pause);
        _stopButton.Accepting += (_, _) => TriggerAction(ToolBarAction.Stop);
        _chatButton.Accepting += (_, _) => TriggerAction(ToolBarAction.Chat);
        _statsButton.Accepting += (_, _) => TriggerAction(ToolBarAction.Stats);

        _container.Add(_newButton, _pauseButton, _stopButton, _chatButton, _statsButton);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>工具栏操作请求事件。</summary>
    public event Action<ToolBarAction>? ActionRequested;

    /// <summary>触发指定操作（按钮点击和快捷键统一入口）。</summary>
    /// <param name="action">操作类型。</param>
    public void TriggerAction(ToolBarAction action) => ActionRequested?.Invoke(action);

    /// <summary>设置运行状态——运行中禁用 New，启用 Pause/Stop；空闲反之。</summary>
    /// <param name="running">true 表示 Agent 运行中。</param>
    public void SetRunning(bool running)
    {
        _newButton.Enabled = !running;
        _pauseButton.Enabled = running;
        _stopButton.Enabled = running;
    }

    /// <inheritdoc />
    public void OnQueueChanged(QueueSnapshot snapshot)
    {
    }

    /// <inheritdoc />
    public void OnResize(int cols, int rows)
    {
    }
}
