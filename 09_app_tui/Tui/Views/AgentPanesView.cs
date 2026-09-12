namespace JoinCode.Tui.Views;

/// <summary>
/// 多 Agent 面板组件 — 显示多个子代理的输出面板。
/// 对齐 TS 原版 的 AgentPanes 设计，每个 agent 独立面板，支持切换。
/// </summary>
public sealed class AgentPanesView : ITuiComponent
{
    private readonly View _container;
    private readonly Label _agentNameLabel;
    private readonly View _contentArea;
    private readonly Dictionary<string, OutputView> _agentPanes = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeAgent;

    /// <summary>
    /// 创建 AgentPanesView。
    /// </summary>
    /// <param name="height">面板高度，0 表示自动填充。</param>
    public AgentPanesView(int height = 0)
    {
        _container = new View
        {
            Width = Dim.Fill(),
            Height = height > 0 ? height : Dim.Fill(),
            Visible = false,
        };

        _agentNameLabel = new Label
        {
            Text = "",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
        };

        _contentArea = new View
        {
            X = 0,
            Y = Pos.Bottom(_agentNameLabel),
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        _container.Add(_agentNameLabel, _contentArea);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>注册 agent 面板。</summary>
    /// <param name="agentId">agent ID。</param>
    /// <param name="displayName">显示名称。</param>
    public void RegisterAgent(string agentId, string displayName)
    {
        if (_agentPanes.ContainsKey(agentId)) return;

        var pane = new OutputView();
        _agentPanes[agentId] = pane;

        if (_activeAgent is null)
        {
            SwitchTo(agentId);
        }
    }

    /// <summary>注销 agent 面板。</summary>
    /// <param name="agentId">agent ID。</param>
    public void UnregisterAgent(string agentId)
    {
        if (!_agentPanes.TryGetValue(agentId, out var pane)) return;
        _agentPanes.Remove(agentId);
        _contentArea.Remove(pane.TerminalView);

        if (string.Equals(_activeAgent, agentId, StringComparison.OrdinalIgnoreCase))
        {
            _activeAgent = null;
            _container.Visible = _agentPanes.Count > 0;
            if (_agentPanes.Count > 0)
            {
                SwitchTo(_agentPanes.Keys.First());
            }
        }
    }

    /// <summary>追加输出到指定 agent 面板。</summary>
    /// <param name="agentId">agent ID。</param>
    /// <param name="line">输出行。</param>
    public void AppendLine(string agentId, string line)
    {
        if (_agentPanes.TryGetValue(agentId, out var pane))
        {
            pane.AppendLine(line);
        }
    }

    /// <summary>切换到指定 agent 面板。</summary>
    /// <param name="agentId">agent ID。</param>
    public void SwitchTo(string agentId)
    {
        if (!_agentPanes.TryGetValue(agentId, out var pane)) return;

        if (_activeAgent is not null && _agentPanes.TryGetValue(_activeAgent, out var oldPane))
        {
            _contentArea.Remove(oldPane.TerminalView);
        }

        _activeAgent = agentId;
        _container.Visible = true;
        _agentNameLabel.Text = $"[{agentId}]";
        _contentArea.Add(pane.TerminalView);
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
