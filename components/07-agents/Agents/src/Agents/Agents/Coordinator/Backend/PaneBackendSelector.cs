namespace Core.Agents.Coordinator;

/// <summary>
/// 终端面板后端选择器 — 运行时自动选择可用的后端（Tmux > iTerm2 > InProcess）
/// <para>桥接 [Register] 自动注册与运行时条件选择逻辑</para>
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IPaneBackend))]
public sealed class PaneBackendSelector : JoinCode.Abstractions.Interfaces.IPaneBackend
{
    private readonly JoinCode.Abstractions.Interfaces.IPaneBackend _backend;

    public PaneBackendSelector(
        TmuxPaneBackend tmuxBackend,
        ITerm2PaneBackend iterm2Backend,
        InProcessPaneBackend inProcessBackend)
    {
        _backend = tmuxBackend.IsAvailable ? tmuxBackend
            : iterm2Backend.IsAvailable ? iterm2Backend
            : inProcessBackend;
    }

    public JoinCode.Abstractions.Interfaces.BackendType BackendType => _backend.BackendType;

    public Task<JoinCode.Abstractions.Interfaces.CreatePaneResult> CreateTeammatePaneAsync(
        string teammateId, string command, CancellationToken cancellationToken = default)
        => _backend.CreateTeammatePaneAsync(teammateId, command, cancellationToken);

    public Task SendCommandToPaneAsync(string paneId, string command, CancellationToken cancellationToken = default)
        => _backend.SendCommandToPaneAsync(paneId, command, cancellationToken);

    public Task SetPaneBorderColorAsync(string paneId, string colorHex, CancellationToken cancellationToken = default)
        => _backend.SetPaneBorderColorAsync(paneId, colorHex, cancellationToken);

    public Task SetPaneTitleAsync(string paneId, string title, CancellationToken cancellationToken = default)
        => _backend.SetPaneTitleAsync(paneId, title, cancellationToken);

    public Task KillPaneAsync(string paneId, CancellationToken cancellationToken = default)
        => _backend.KillPaneAsync(paneId, cancellationToken);

    public Task RebalancePanesAsync(CancellationToken cancellationToken = default)
        => _backend.RebalancePanesAsync(cancellationToken);
}
