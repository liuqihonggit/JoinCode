namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 终端后端类型枚举
/// </summary>
public enum BackendType {
    [EnumValue("in_process")]
    InProcess,
    [EnumValue("tmux")]
    Tmux,
    [EnumValue("iterm2")]
    ITerm2
}

/// <summary>
/// 创建面板结果
/// </summary>
public sealed record CreatePaneResult {
    /// <summary>获取面板 ID。</summary>
    public required string PaneId { get; init; }
    /// <summary>获取后端类型。</summary>
    public required BackendType BackendType { get; init; }
}

/// <summary>
/// 终端面板后端接口 - 提供终端多路复用器的面板管理功能（tmux/iTerm2/进程内）
/// </summary>
public interface IPaneBackend {
    /// <summary>获取后端类型。</summary>
    BackendType BackendType { get; }

    /// <summary>异步创建队友面板。</summary>
    Task<CreatePaneResult> CreateTeammatePaneAsync(string teammateId, string command, CancellationToken cancellationToken = default);

    /// <summary>异步向指定面板发送命令。</summary>
    Task SendCommandToPaneAsync(string paneId, string command, CancellationToken cancellationToken = default);

    /// <summary>异步设置面板边框颜色。</summary>
    Task SetPaneBorderColorAsync(string paneId, string colorHex, CancellationToken cancellationToken = default);

    /// <summary>异步设置面板标题。</summary>
    Task SetPaneTitleAsync(string paneId, string title, CancellationToken cancellationToken = default);

    /// <summary>异步终止指定面板。</summary>
    Task KillPaneAsync(string paneId, CancellationToken cancellationToken = default);

    /// <summary>异步重新平衡所有面板布局。</summary>
    Task RebalancePanesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 队友布局管理器接口 - 管理多 Agent 场景下的终端面板布局
/// </summary>
public interface ITeammateLayoutManager {
    /// <summary>异步创建队友面板。</summary>
    Task<CreatePaneResult> CreateTeammatePaneAsync(string teammateId, string agentType, string command, CancellationToken cancellationToken = default);

    /// <summary>为队友分配颜色。</summary>
    string AssignTeammateColor(string teammateId);

    /// <summary>异步移除队友面板。</summary>
    Task RemoveTeammatePaneAsync(string teammateId, CancellationToken cancellationToken = default);

    /// <summary>异步重新平衡布局。</summary>
    Task RebalanceLayoutAsync(CancellationToken cancellationToken = default);

    /// <summary>获取当前后端类型。</summary>
    BackendType CurrentBackendType { get; }
}
