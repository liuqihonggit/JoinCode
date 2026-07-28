
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Teammate 运行时状态 - 聚合团队信息和运行时状态
/// </summary>
public sealed record TeammateStatus
{
    public required string AgentId { get; init; }
    public required string TeamId { get; init; }
    public string? TeamName { get; init; }
    public string? AgentType { get; init; }
    public string? DisplayName { get; init; }
    public AgentStatus Status { get; init; } = AgentStatus.Pending;
    public string? Role { get; init; }
    public string? ColorHex { get; init; }
    public string? WorktreePath { get; init; }
    public string? PermissionMode { get; init; }
    public DateTime? StartedAt { get; init; }
    public string? LastActivity { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// 团队管理器接口
/// </summary>
public interface ITeamManager : IDisposable
{
    /// <summary>
    /// 创建团队
    /// </summary>
    Task<OperationResult<TeamInfo?>> CreateTeamAsync(
        string teamName,
        string? description = null,
        List<string>? initialMembers = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除团队
    /// </summary>
    Task<OperationResult<TeamInfo?>> DeleteTeamAsync(
        string teamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取团队信息
    /// </summary>
    Task<TeamInfo?> GetTeamAsync(
        string teamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出所有团队
    /// </summary>
    Task<IReadOnlyList<TeamInfo>> ListTeamsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加团队成员
    /// </summary>
    Task<OperationResult<TeamInfo?>> AddTeamMemberAsync(
        string teamId,
        string agentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除团队成员
    /// </summary>
    Task<OperationResult<TeamInfo?>> RemoveTeamMemberAsync(
        string teamId,
        string agentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取团队成员
    /// </summary>
    Task<IReadOnlyList<string>> GetTeamMembersAsync(
        string teamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送消息给团队成员
    /// </summary>
    Task<OperationResult<TeamInfo?>> SendMessageAsync(
        string teamId,
        string senderId,
        string content,
        string? messageType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送消息给指定代理
    /// </summary>
    Task<OperationResult<TeamInfo?>> SendMessageToAgentAsync(
        string targetAgentId,
        string senderId,
        string content,
        string? messageType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取团队消息历史
    /// </summary>
    Task<IReadOnlyList<TeamMessage>> GetTeamMessagesAsync(
        string teamId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 广播消息给所有团队成员
    /// </summary>
    Task<OperationResult<TeamInfo?>> BroadcastMessageAsync(
        string teamId,
        string senderId,
        string content,
        string? messageType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置成员活跃状态
    /// </summary>
    Task<OperationResult<TeamInfo?>> SetMemberActiveAsync(
        string teamId,
        string agentId,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取团队允许路径
    /// </summary>
    Task<IReadOnlyList<TeamAllowedPath>> GetTeamAllowedPathsAsync(
        string teamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加团队允许路径
    /// </summary>
    Task<OperationResult<TeamInfo?>> AddTeamAllowedPathAsync(
        string teamId,
        string path,
        AccessLevel accessLevel = AccessLevel.Read,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定团队所有成员的运行时状态
    /// </summary>
    Task<IReadOnlyList<TeammateStatus>> GetTeammateStatusesAsync(
        string teamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有团队所有成员的运行时状态
    /// </summary>
    Task<IReadOnlyList<TeammateStatus>> GetAllTeammateStatusesAsync(
        CancellationToken cancellationToken = default);
}
