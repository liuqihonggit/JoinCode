namespace Core.Agents.Coordinator;

/// <summary>
/// Teammate 状态构建器 — 合并团队注册表与运行时观察器数据，生成 TeammateStatus 列表
/// </summary>
internal sealed class TeammateStatusBuilder
{
    private readonly TeamRegistry _registry;
    private readonly Func<ITeammateObserver?> _resolveObserver;

    /// <summary>
    /// 构造 Teammate 状态构建器实例
    /// </summary>
    /// <param name="registry">团队注册表</param>
    /// <param name="resolveObserver">延迟解析 ITeammateObserver 的委托，打破循环依赖</param>
    public TeammateStatusBuilder(TeamRegistry registry, Func<ITeammateObserver?> resolveObserver)
    {
        _registry = registry;
        _resolveObserver = resolveObserver;
    }

    /// <summary>
    /// 获取指定团队所有 Teammate 的状态，合并运行时观察器数据
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Teammate 状态只读列表</returns>
    public async Task<IReadOnlyList<TeammateStatus>> GetTeammateStatusesAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return Array.Empty<TeammateStatus>();
        }

        var team = room.Info;
        var memberDetails = room.MemberDetails;

        var runningTeammates = _resolveObserver() is { } observer
            ? await observer.GetRunningTeammatesAsync().ConfigureAwait(false)
            : [];
        var runningMap = runningTeammates.ToDictionary(t => t.Id);

        var statuses = memberDetails.Values
            .Select(md => BuildTeammateStatus(md, team, runningMap))
            .ToList();

        return statuses;
    }

    /// <summary>
    /// 获取所有团队的所有 Teammate 状态，合并运行时观察器数据
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>所有 Teammate 状态只读列表</returns>
    public async Task<IReadOnlyList<TeammateStatus>> GetAllTeammateStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        var runningTeammates = _resolveObserver() is { } observer
            ? await observer.GetRunningTeammatesAsync().ConfigureAwait(false)
            : [];

        var runningMap = runningTeammates.ToDictionary(t => t.Id);

        var statuses = new List<TeammateStatus>();

        foreach (var room in _registry.Rooms)
        {
            var team = room.Info;
            var memberDetails = room.MemberDetails;

            foreach (var md in memberDetails.Values)
            {
                statuses.Add(BuildTeammateStatus(md, team, runningMap));
            }
        }

        return statuses;
    }

    private static TeammateStatus BuildTeammateStatus(
        TeamMemberInfo memberInfo,
        TeamInfo team,
        Dictionary<string, TeammateInfo> runningMap)
    {
        runningMap.TryGetValue(memberInfo.AgentId, out var running);

        return new TeammateStatus
        {
            AgentId = memberInfo.AgentId,
            TeamId = team.TeamId,
            TeamName = team.TeamName,
            Role = memberInfo.Role,
            ColorHex = memberInfo.Color ?? running?.ColorHex,
            DisplayName = running?.DisplayName ?? memberInfo.AgentId,
            Status = running?.State ?? AgentStatus.Pending,
            IsActive = memberInfo.IsActive,
            StartedAt = running?.StartedAt,
            LastActivity = running?.LastActivity,
            AgentType = running?.SpinnerVerb,
            WorktreePath = null,
            PermissionMode = null
        };
    }
}
