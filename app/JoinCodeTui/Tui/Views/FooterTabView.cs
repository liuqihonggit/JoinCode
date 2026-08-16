namespace JoinCode.Tui.Views;

/// <summary>
/// 底部面板标签类型 — 对应 Log/Files/Memory/Settings 四个面板。
/// </summary>
public enum FooterTab
{
    /// <summary>日志面板</summary>
    [EnumValue("log")] Log,
    /// <summary>文件浏览面板</summary>
    [EnumValue("files")] Files,
    /// <summary>记忆/上下文面板</summary>
    [EnumValue("memory")] Memory,
    /// <summary>设置面板</summary>
    [EnumValue("settings")] Settings,
}

/// <summary>
/// 底部状态栏组件 — Tab 切换（Log/Files/Memory/Settings）+ 运行时长计时器。
/// Tab 用 Pos.Right 链式水平排列（相对定位），计时器放在 Tab 右侧。
/// 对齐 Agent TUI 设计规范的底部状态栏区域。
/// </summary>
public sealed class FooterTabView : ITuiComponent
{
    private readonly View _container;
    private readonly Button _logTab;
    private readonly Button _filesTab;
    private readonly Button _memoryTab;
    private readonly Button _settingsTab;
    private readonly Label _timerLabel;
    private FooterTab _activeTab = FooterTab.Log;

    /// <summary>
    /// 创建 FooterTabView。
    /// </summary>
    public FooterTabView()
    {
        _container = new View
        {
            Width = Dim.Fill(),
            Height = 1,
        };

        _logTab = new Button { Text = "[Log]", X = 0, Y = 0 };
        _filesTab = new Button { Text = "Files", X = Pos.Right(_logTab) + 1, Y = 0 };
        _memoryTab = new Button { Text = "Memory", X = Pos.Right(_filesTab) + 1, Y = 0 };
        _settingsTab = new Button { Text = "Settings", X = Pos.Right(_memoryTab) + 1, Y = 0 };

        _timerLabel = new Label
        {
            Text = "⏱ 00:00:00",
            X = Pos.Right(_settingsTab) + 2,
            Y = 0,
        };

        _logTab.Accepting += (_, _) => SwitchTo(FooterTab.Log);
        _filesTab.Accepting += (_, _) => SwitchTo(FooterTab.Files);
        _memoryTab.Accepting += (_, _) => SwitchTo(FooterTab.Memory);
        _settingsTab.Accepting += (_, _) => SwitchTo(FooterTab.Settings);

        _container.Add(_logTab, _filesTab, _memoryTab, _settingsTab, _timerLabel);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>Tab 切换事件。</summary>
    public event Action<FooterTab>? TabSwitched;

    /// <summary>切换到指定 Tab（按钮点击和外部调用统一入口）。</summary>
    /// <param name="tab">目标标签。</param>
    public void SwitchTo(FooterTab tab)
    {
        _activeTab = tab;
        UpdateTabStyles();
        TabSwitched?.Invoke(tab);
    }

    /// <summary>设置运行时长计时器显示。</summary>
    /// <param name="elapsed">已运行时长。</param>
    public void SetElapsedTime(TimeSpan elapsed)
    {
        _timerLabel.Text = $"⏱ {elapsed:hh\\:mm\\:ss}";
    }

    /// <inheritdoc />
    public void OnQueueChanged(QueueSnapshot snapshot)
    {
    }

    /// <inheritdoc />
    public void OnResize(int cols, int rows)
    {
    }

    private void UpdateTabStyles()
    {
        _logTab.Text = _activeTab == FooterTab.Log ? "[Log]" : "Log";
        _filesTab.Text = _activeTab == FooterTab.Files ? "[Files]" : "Files";
        _memoryTab.Text = _activeTab == FooterTab.Memory ? "[Memory]" : "Memory";
        _settingsTab.Text = _activeTab == FooterTab.Settings ? "[Settings]" : "Settings";
    }
}
