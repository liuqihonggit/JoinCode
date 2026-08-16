namespace JoinCode.Tui.Views;

/// <summary>
/// 状态栏组件 — 显示当前模式、队列长度、Agent 状态等信息。
/// 对齐 claude code 的 StatusBar 组件。
/// </summary>
public sealed class StatusBarView : ITuiComponent
{
    private readonly View _container;
    private readonly Label _statusLabel;
    private string _mode = "auto";
    private int _queueCount;
    private string _agentStatus = "";
    private int _sessionId = 1;

    /// <summary>
    /// 创建 StatusBarView。
    /// </summary>
    public StatusBarView()
    {
        _container = new View
        {
            Width = Dim.Fill(),
            Height = 1,
        };

        _statusLabel = new Label
        {
            Text = "",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
        };

        _container.Add(_statusLabel);
        RefreshDisplay();
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>设置当前权限模式。</summary>
    /// <param name="mode">模式名称。</param>
    public void SetMode(string mode)
    {
        _mode = mode;
        RefreshDisplay();
    }

    /// <summary>设置 Agent 状态文本。</summary>
    /// <param name="status">状态描述。</param>
    public void SetAgentStatus(string status)
    {
        _agentStatus = status;
        RefreshDisplay();
    }

    /// <summary>设置当前会话ID。</summary>
    /// <param name="id">会话序号。</param>
    public void SetSessionId(int id)
    {
        _sessionId = id;
        RefreshDisplay();
    }

    /// <inheritdoc />
    public void OnQueueChanged(QueueSnapshot snapshot)
    {
        _queueCount = snapshot.TotalCount;
        RefreshDisplay();
    }

    /// <inheritdoc />
    public void OnResize(int cols, int rows)
    {
        _container.Width = Dim.Fill();
    }

    private void RefreshDisplay()
    {
        var queuePart = _queueCount > 0 ? $" │ 队列:{_queueCount}" : "";
        var agentPart = string.IsNullOrEmpty(_agentStatus) ? "" : $" │ {_agentStatus}";
        _statusLabel.Text = $"⚡ AgentOS v1.0 │ {_mode}{queuePart}{agentPart} │ Session: #{_sessionId:D3}";
    }
}
