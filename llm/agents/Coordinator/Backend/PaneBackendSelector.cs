namespace Core.Agents.Coordinator;

/// <summary>
/// 终端面板后端选择器 — 运行时自动选择可用的后端（Tmux > iTerm2 > InProcess）
/// <para>桥接 [Register] 自动注册与运行时条件选择逻辑</para>
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IPaneBackend), ServiceLifetime.Singleton)]
public sealed partial class PaneBackendSelector : ServiceEntity, JoinCode.Abstractions.Interfaces.IPaneBackend
{
    private readonly JoinCode.Abstractions.Interfaces.IPaneBackend _backend;

    /// <summary>
    /// 构造面板后端选择器，按 Tmux &gt; iTerm2 &gt; InProcess 优先级选取首个可用后端
    /// </summary>
    /// <param name="tmuxBackend">tmux 后端候选</param>
    /// <param name="iterm2Backend">iTerm2 后端候选</param>
    /// <param name="inProcessBackend">进程内后端候选（始终可用作兜底）</param>
    public PaneBackendSelector(
        TmuxPaneBackend tmuxBackend,
        ITerm2PaneBackend iterm2Backend,
        InProcessPaneBackend inProcessBackend)
    {
        _backend = tmuxBackend.IsAvailable ? tmuxBackend
            : iterm2Backend.IsAvailable ? iterm2Backend
            : inProcessBackend;
    }

    /// <summary>当前选中后端的类型标识</summary>
    public JoinCode.Abstractions.Interfaces.BackendType BackendType => _backend.BackendType;

    /// <summary>
    /// 为队友创建面板，委托给选中的后端
    /// </summary>
    /// <param name="teammateId">队友标识</param>
    /// <param name="command">面板启动命令</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>面板创建结果</returns>
    public Task<JoinCode.Abstractions.Interfaces.CreatePaneResult> CreateTeammatePaneAsync(
        string teammateId, string command, CancellationToken cancellationToken = default)
        => _backend.CreateTeammatePaneAsync(teammateId, command, cancellationToken);

    /// <summary>
    /// 向指定面板发送命令，委托给选中的后端
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="command">要发送的命令文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SendCommandToPaneAsync(string paneId, string command, CancellationToken cancellationToken = default)
        => _backend.SendCommandToPaneAsync(paneId, command, cancellationToken);

    /// <summary>
    /// 设置面板边框颜色，委托给选中的后端
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="colorHex">十六进制颜色值</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SetPaneBorderColorAsync(string paneId, string colorHex, CancellationToken cancellationToken = default)
        => _backend.SetPaneBorderColorAsync(paneId, colorHex, cancellationToken);

    /// <summary>
    /// 设置面板标题，委托给选中的后端
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="title">面板标题文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SetPaneTitleAsync(string paneId, string title, CancellationToken cancellationToken = default)
        => _backend.SetPaneTitleAsync(paneId, title, cancellationToken);

    /// <summary>
    /// 关闭指定面板，委托给选中的后端
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task KillPaneAsync(string paneId, CancellationToken cancellationToken = default)
        => _backend.KillPaneAsync(paneId, cancellationToken);

    /// <summary>
    /// 重新平衡面板布局，委托给选中的后端
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public Task RebalancePanesAsync(CancellationToken cancellationToken = default)
        => _backend.RebalancePanesAsync(cancellationToken);
}
