namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 终端后端类型枚举
/// </summary>
public enum BackendType
{
    InProcess,
    Tmux,
    ITerm2
}

/// <summary>
/// 创建面板结果
/// </summary>
public sealed record CreatePaneResult
{
    public required string PaneId { get; init; }
    public required BackendType BackendType { get; init; }
}

/// <summary>
/// 终端面板后端接口 - 提供终端多路复用器的面板管理功能（tmux/iTerm2/进程内）
/// </summary>
public interface IPaneBackend
{
    BackendType BackendType { get; }

    Task<CreatePaneResult> CreateTeammatePaneAsync(string teammateId, string command, CancellationToken cancellationToken = default);

    Task SendCommandToPaneAsync(string paneId, string command, CancellationToken cancellationToken = default);

    Task SetPaneBorderColorAsync(string paneId, string colorHex, CancellationToken cancellationToken = default);

    Task SetPaneTitleAsync(string paneId, string title, CancellationToken cancellationToken = default);

    Task KillPaneAsync(string paneId, CancellationToken cancellationToken = default);

    Task RebalancePanesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 队友布局管理器接口 - 管理多 Agent 场景下的终端面板布局
/// </summary>
public interface ITeammateLayoutManager
{
    Task<CreatePaneResult> CreateTeammatePaneAsync(string teammateId, string agentType, string command, CancellationToken cancellationToken = default);

    string AssignTeammateColor(string teammateId);

    Task RemoveTeammatePaneAsync(string teammateId, CancellationToken cancellationToken = default);

    Task RebalanceLayoutAsync(CancellationToken cancellationToken = default);

    BackendType CurrentBackendType { get; }
}
