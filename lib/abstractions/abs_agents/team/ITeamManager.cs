
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Teammate 运行时状态 - 聚合团队信息和运行时状态
/// </summary>
public sealed record TeammateStatus {
    /// <summary>获取代理标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取团队标识。</summary>
    public required string TeamId { get; init; }
    /// <summary>获取团队名称。</summary>
    public string? TeamName { get; init; }
    /// <summary>获取代理类型。</summary>
    public string? AgentType { get; init; }
    /// <summary>获取显示名称。</summary>
    public string? DisplayName { get; init; }
    /// <summary>获取代理状态。</summary>
    public AgentStatus Status { get; init; } = AgentStatus.Pending;
    /// <summary>获取角色。</summary>
    public string? Role { get; init; }
    /// <summary>获取颜色十六进制值。</summary>
    public string? ColorHex { get; init; }
    /// <summary>获取工作树路径。</summary>
    public string? WorktreePath { get; init; }
    /// <summary>获取权限模式。</summary>
    public string? PermissionMode { get; init; }
    /// <summary>获取启动时间。</summary>
    public DateTime? StartedAt { get; init; }
    /// <summary>获取最后活动。</summary>
    public string? LastActivity { get; init; }
    /// <summary>获取是否活跃。</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>投影为 AgentCoreIdentity（Role 从 string 转换为枚举）</summary>
    public AgentCoreIdentity ToIdentity() => new(AgentId, DisplayName, Role is not null && Enum.TryParse<AgentRole>(Role, out var role) ? role : null);

    /// <summary>投影为 TeamIdentity</summary>
    public TeamIdentity ToTeamIdentity() => new(TeamId, TeamName);
}

/// <summary>
/// 团队管理器接口
/// </summary>
public interface ITeamManager : IDisposable {
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

    /// <summary>
    /// 获取聊天室信息 — 团队的聊天室视图，包含房间名和成员显示名列表 — ADR 0109
    /// </summary>
    Task<ChatRoomInfo?> GetChatRoomInfoAsync(
        string teamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤回团队消息 — 对标 QQ 消息撤回（2 分钟内可撤回）— ADR 0109 决策11。
    /// <para>权限：发送者本人或管理员/群主可撤回。</para>
    /// <para>效果：消息 Visibility=Hidden + 广播撤回系统通知。</para>
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="messageId">消息标识</param>
    /// <param name="revokerId">撤回者标识</param>
    /// <param name="reason">撤回原因（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>撤回结果（成功包含团队信息）</returns>
    Task<OperationResult<TeamInfo?>> RevokeMessageAsync(
        string teamId,
        string messageId,
        string revokerId,
        string? reason = null,
        CancellationToken cancellationToken = default);
}