
namespace Core.Agents.Coordinator;

/// <summary>
/// 团队管理器实现
/// 使用单一锁保护成员和消息操作，消除多锁排序风险
/// </summary>
[Register]
public sealed class TeamManager : ITeamManager, IDisposable
{
    private readonly ConcurrentDictionary<string, TeamInfo> _teams = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _teamMembers = new();
    private readonly ConcurrentDictionary<string, List<TeamMessage>> _teamMessages = new();
    private readonly ConcurrentDictionary<string, string> _agentToTeam = new();
    private readonly ConcurrentDictionary<string, string> _teamSessions = new();
    private readonly ConcurrentDictionary<string, List<TeamAllowedPath>> _teamAllowedPaths = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, TeamMemberInfo>> _teamMemberDetails = new();
    private readonly SemaphoreSlim _lock;
    private readonly ITelemetryService? _telemetryService;
    private readonly ITeammateMailboxService? _mailboxService;
    private readonly IServiceProvider? _serviceProvider;
    private readonly IClockService _clock;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private int _teamCounter;
    private int _messageCounter;

    /// <summary>
    /// 延迟解析 ITeammateObserver，打破循环依赖：
    /// AgentCoordinator → ITeammateReconnectService → ITeamManager → ITeammateObserver → AgentCoordinator
    /// </summary>
    private ITeammateObserver? ResolvedTeammateObserver =>
        _serviceProvider?.GetService(typeof(ITeammateObserver)) as ITeammateObserver;

    public TeamManager(IClockService clock, ITelemetryService? telemetryService = null, ITeammateMailboxService? mailboxService = null, IServiceProvider? serviceProvider = null, ISubAgentContextAccessor? subAgentContextAccessor = null)
    {
        _lock = new SemaphoreSlim(1, 1);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _telemetryService = telemetryService;
        _mailboxService = mailboxService;
        _serviceProvider = serviceProvider;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
    }

    public Task<OperationResult<TeamInfo?>> CreateTeamAsync(
        string teamName,
        string? description = null,
        List<string>? initialMembers = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamName))
        {
            return Task.FromResult(OperationResult<TeamInfo?>.Fail("团队名称不能为空"));
        }

        // 单团队限制：当前会话已存在团队时不允许再创建（对齐 TS TeamCreateTool）
        var sessionId = _subAgentContextAccessor.Current?.SessionId;
        if (sessionId is not null)
        {
            var existingTeamForSession = _teamSessions.FirstOrDefault(kvp => kvp.Value == sessionId);
            if (existingTeamForSession.Key is not null)
            {
                return Task.FromResult(OperationResult<TeamInfo?>.Fail("已在团队中，请先使用 TeamDelete 删除当前团队"));
            }
        }

        // 名称唯一性检查（对齐 TS generateUniqueTeamName）
        var nameConflict = _teams.Values.FirstOrDefault(t => string.Equals(t.TeamName, teamName, StringComparison.OrdinalIgnoreCase));
        if (nameConflict is not null)
        {
            return Task.FromResult(OperationResult<TeamInfo?>.Fail($"团队名称 '{teamName}' 已存在，请使用其他名称"));
        }

        var teamId = GenerateTeamId();
        var members = initialMembers?.ToHashSet() ?? new HashSet<string>();
        var memberDetails = members.ToDictionary(
            m => m,
            m => new TeamMemberInfo { AgentId = m, JoinedAt = _clock.GetUtcNow() });

        var leadAgentId = initialMembers?.FirstOrDefault();

        var team = new TeamInfo
        {
            TeamId = teamId,
            TeamName = teamName,
            Description = description,
            LeadAgentId = leadAgentId,
            Members = members.ToList(),
            MemberDetails = memberDetails.Values.ToList(),
            CreatedAt = _clock.GetUtcNow(),
            LastActivityAt = _clock.GetUtcNow()
        };

        _teams[teamId] = team;
        _teamMembers[teamId] = members;
        _teamMessages[teamId] = new List<TeamMessage>();
        _teamMemberDetails[teamId] = memberDetails;
        _teamAllowedPaths[teamId] = new List<TeamAllowedPath>();

        if (sessionId is not null)
        {
            _teamSessions[teamId] = sessionId;
        }

        if (initialMembers != null)
        {
            foreach (var member in initialMembers)
            {
                _agentToTeam[member] = teamId;
            }
        }

        return Task.FromResult(OperationResult<TeamInfo?>.Ok(team));
    }

    public Task<OperationResult<TeamInfo?>> DeleteTeamAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        if (!_teams.TryGetValue(teamId, out var team))
        {
            RecordTeamMetrics("delete", false);
            return Task.FromResult(OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在"));
        }

        // Active member 安全检查（对齐 TS TeamDeleteTool）
        if (_teamMemberDetails.TryGetValue(teamId, out var memberDetails))
        {
            var activeMembers = memberDetails.Values.Where(m => m.IsActive).ToList();
            if (activeMembers.Count > 0)
            {
                var activeNames = string.Join(", ", activeMembers.Select(m => m.AgentId));
                return Task.FromResult(OperationResult<TeamInfo?>.Fail($"团队仍有活跃成员: {activeNames}，请先优雅关闭所有队友再删除团队"));
            }
        }

        _teams.TryRemove(teamId, out _);

        if (_teamMembers.TryRemove(teamId, out var members))
        {
            foreach (var member in members)
            {
                _agentToTeam.TryRemove(member, out _);
            }
        }

        _teamMessages.TryRemove(teamId, out _);

        RecordTeamMetrics("delete", true);
        return Task.FromResult(OperationResult<TeamInfo?>.Ok(team));
    }

    public Task<TeamInfo?> GetTeamAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        _teams.TryGetValue(teamId, out var team);
        return Task.FromResult(team);
    }

    public Task<IReadOnlyList<TeamInfo>> ListTeamsAsync(
        CancellationToken cancellationToken = default)
    {
        var teams = _teams.Values.ToList();
        return Task.FromResult<IReadOnlyList<TeamInfo>>(teams);
    }

    public async Task<OperationResult<TeamInfo?>> AddTeamMemberAsync(
        string teamId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        if (!_teams.TryGetValue(teamId, out var team))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var members = _teamMembers.GetOrAdd(teamId, _ => new HashSet<string>());
        var memberDetails = _teamMemberDetails.GetOrAdd(teamId, _ => new Dictionary<string, TeamMemberInfo>());

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (members.Contains(agentId))
            {
                return OperationResult<TeamInfo?>.Fail($"代理 {agentId} 已经是团队成员");
            }

            members.Add(agentId);
            memberDetails[agentId] = new TeamMemberInfo { AgentId = agentId, JoinedAt = _clock.GetUtcNow() };
        }
        finally
        {
            _lock.Release();
        }

        _teams[teamId] = team with
        {
            Members = members.ToList(),
            MemberDetails = memberDetails.Values.ToList(),
            LastActivityAt = _clock.GetUtcNow()
        };

        _agentToTeam[agentId] = teamId;

        return OperationResult<TeamInfo?>.Ok(_teams[teamId]);
    }

    public async Task<OperationResult<TeamInfo?>> RemoveTeamMemberAsync(
        string teamId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        if (!_teams.TryGetValue(teamId, out var team))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        if (!_teamMembers.TryGetValue(teamId, out var members))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 没有成员列表");
        }

        var memberDetails = _teamMemberDetails.GetOrAdd(teamId, _ => new Dictionary<string, TeamMemberInfo>());

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!members.Remove(agentId))
            {
                return OperationResult<TeamInfo?>.Fail($"代理 {agentId} 不是团队成员");
            }

            memberDetails.Remove(agentId);
        }
        finally
        {
            _lock.Release();
        }

        _agentToTeam.TryRemove(agentId, out _);

        _teams[teamId] = team with
        {
            Members = members.ToList(),
            MemberDetails = memberDetails.Values.ToList(),
            LastActivityAt = _clock.GetUtcNow()
        };

        return OperationResult<TeamInfo?>.Ok(_teams[teamId]);
    }

    public Task<IReadOnlyList<string>> GetTeamMembersAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        if (!_teamMembers.TryGetValue(teamId, out var members))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        return Task.FromResult<IReadOnlyList<string>>(members.ToList());
    }

    public async Task<OperationResult<TeamInfo?>> SendMessageAsync(
        string teamId,
        string senderId,
        string content,
        string? messageType = null,
        CancellationToken cancellationToken = default)
    {
        if (!_teams.TryGetValue(teamId, out var team))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var message = new TeamMessage
        {
            MessageId = GenerateMessageId(),
            TeamId = teamId,
            SenderId = senderId,
            Content = content,
            MessageType = messageType ?? "broadcast",
            Timestamp = _clock.GetUtcNow()
        };

        var messages = _teamMessages.GetOrAdd(teamId, _ => new List<TeamMessage>());

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_teamMembers.TryGetValue(teamId, out var members) || !members.Contains(senderId))
            {
                return OperationResult<TeamInfo?>.Fail($"发送者 {senderId} 不是团队成员");
            }

            messages.Add(message);
        }
        finally
        {
            _lock.Release();
        }

        _teams[teamId] = team with { LastActivityAt = _clock.GetUtcNow() };

        await PersistTeamMessageToMailboxAsync(teamId, message, cancellationToken).ConfigureAwait(false);

        return OperationResult<TeamInfo?>.Ok(_teams[teamId]);
    }

    public async Task<OperationResult<TeamInfo?>> SendMessageToAgentAsync(
        string targetAgentId,
        string senderId,
        string content,
        string? messageType = null,
        CancellationToken cancellationToken = default)
    {
        if (!_agentToTeam.TryGetValue(targetAgentId, out var teamId))
        {
            return OperationResult<TeamInfo?>.Fail($"代理 {targetAgentId} 不属于任何团队");
        }

        if (!_teams.TryGetValue(teamId, out var team))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var message = new TeamMessage
        {
            MessageId = GenerateMessageId(),
            TeamId = teamId,
            SenderId = senderId,
            Content = $"[私信给 {targetAgentId}] {content}",
            MessageType = messageType ?? "direct",
            Timestamp = _clock.GetUtcNow()
        };

        var messages = _teamMessages.GetOrAdd(teamId, _ => new List<TeamMessage>());

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_teamMembers.TryGetValue(teamId, out var members) || !members.Contains(senderId))
            {
                return OperationResult<TeamInfo?>.Fail($"发送者 {senderId} 不是团队成员");
            }

            messages.Add(message);
        }
        finally
        {
            _lock.Release();
        }

        await PersistDirectMessageToMailboxAsync(targetAgentId, senderId, content, messageType ?? "direct", teamId, cancellationToken).ConfigureAwait(false);

        return OperationResult<TeamInfo?>.Ok(team);
    }

    public async Task<IReadOnlyList<TeamMessage>> GetTeamMessagesAsync(
        string teamId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_teamMessages.TryGetValue(teamId, out var messages))
        {
            return Array.Empty<TeamMessage>();
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return messages
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<OperationResult<TeamInfo?>> BroadcastMessageAsync(
        string teamId,
        string senderId,
        string content,
        string? messageType = null,
        CancellationToken cancellationToken = default)
    {
        if (!_teams.TryGetValue(teamId, out var team))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var message = new TeamMessage
        {
            MessageId = GenerateMessageId(),
            TeamId = teamId,
            SenderId = senderId,
            Content = $"[广播] {content}",
            MessageType = messageType ?? "broadcast",
            Timestamp = _clock.GetUtcNow()
        };

        var messages = _teamMessages.GetOrAdd(teamId, _ => new List<TeamMessage>());

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_teamMembers.TryGetValue(teamId, out var members) || !members.Contains(senderId))
            {
                return OperationResult<TeamInfo?>.Fail($"发送者 {senderId} 不是团队成员");
            }

            messages.Add(message);
        }
        finally
        {
            _lock.Release();
        }

        _teams[teamId] = team with { LastActivityAt = _clock.GetUtcNow() };

        await PersistTeamMessageToMailboxAsync(teamId, message, cancellationToken).ConfigureAwait(false);

        return OperationResult<TeamInfo?>.Ok(_teams[teamId]);
    }

    private string GenerateTeamId()
    {
        var counter = Interlocked.Increment(ref _teamCounter);
        return $"team_{counter:D4}_{_clock.GetUtcNow():yyyyMMddHHmmss}";
    }

    private string GenerateMessageId()
    {
        var counter = Interlocked.Increment(ref _messageCounter);
        return $"msg_{counter:D6}_{_clock.GetUtcNow():yyyyMMddHHmmssfff}";
    }

    private void RecordTeamMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("team.operation.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Team operation count");

    private async Task PersistTeamMessageToMailboxAsync(string teamId, TeamMessage message, CancellationToken cancellationToken)
    {
        if (_mailboxService is null) return;
        if (!_teamMembers.TryGetValue(teamId, out var members)) return;
        if (!_teamSessions.TryGetValue(teamId, out var sessionId)) return;

        var tasks = members
            .Where(m => m != message.SenderId)
            .Select(m => _mailboxService.SendAsync(new MailboxSendRequest
            {
                FromAgentId = message.SenderId,
                ToAgentId = m,
                MessageType = message.MessageType,
                Content = message.Content,
                SessionId = sessionId
            }, cancellationToken));

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Trace.WriteLine($"Broadcast message failed: {ex.Message}");
        }
    }

    private async Task PersistDirectMessageToMailboxAsync(
        string targetAgentId, string senderId, string content, string messageType, string teamId,
        CancellationToken cancellationToken)
    {
        if (_mailboxService is null) return;
        if (!_teamSessions.TryGetValue(teamId, out var sessionId)) return;

        try
        {
            await _mailboxService.SendAsync(new MailboxSendRequest
            {
                FromAgentId = senderId,
                ToAgentId = targetAgentId,
                MessageType = messageType,
                Content = content,
                SessionId = sessionId
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex2) when (ex2 is not OperationCanceledException)
        {
            System.Diagnostics.Trace.WriteLine($"Persist direct message to mailbox failed: {ex2.Message}");
        }
    }

    public async Task<OperationResult<TeamInfo?>> SetMemberActiveAsync(
        string teamId,
        string agentId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (!_teams.TryGetValue(teamId, out var team))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var memberDetails = _teamMemberDetails.GetOrAdd(teamId, _ => new Dictionary<string, TeamMemberInfo>());

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!memberDetails.TryGetValue(agentId, out var existing))
            {
                return OperationResult<TeamInfo?>.Fail($"代理 {agentId} 不是团队成员");
            }

            memberDetails[agentId] = existing with { IsActive = isActive };
        }
        finally
        {
            _lock.Release();
        }

        _teams[teamId] = team with
        {
            MemberDetails = memberDetails.Values.ToList(),
            LastActivityAt = _clock.GetUtcNow()
        };

        return OperationResult<TeamInfo?>.Ok(_teams[teamId]);
    }

    public Task<IReadOnlyList<TeamAllowedPath>> GetTeamAllowedPathsAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        if (!_teamAllowedPaths.TryGetValue(teamId, out var paths))
        {
            return Task.FromResult<IReadOnlyList<TeamAllowedPath>>(Array.Empty<TeamAllowedPath>());
        }

        return Task.FromResult<IReadOnlyList<TeamAllowedPath>>(paths.ToList());
    }

    public async Task<OperationResult<TeamInfo?>> AddTeamAllowedPathAsync(
        string teamId,
        string path,
        AccessLevel accessLevel = AccessLevel.Read,
        CancellationToken cancellationToken = default)
    {
        if (!_teams.TryGetValue(teamId, out var team))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return OperationResult<TeamInfo?>.Fail("路径不能为空");
        }

        var paths = _teamAllowedPaths.GetOrAdd(teamId, _ => new List<TeamAllowedPath>());

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = paths.FirstOrDefault(p => p.Path == path);
            if (existing is not null)
            {
                var idx = paths.IndexOf(existing);
                paths[idx] = existing with { AccessLevel = accessLevel };
            }
            else
            {
                paths.Add(new TeamAllowedPath { Path = path, AccessLevel = accessLevel });
            }
        }
        finally
        {
            _lock.Release();
        }

        _teams[teamId] = team with
        {
            AllowedPaths = paths.ToList(),
            LastActivityAt = _clock.GetUtcNow()
        };

        return OperationResult<TeamInfo?>.Ok(_teams[teamId]);
    }

    public async Task<IReadOnlyList<TeammateStatus>> GetTeammateStatusesAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        if (!_teams.TryGetValue(teamId, out var team))
        {
            return Array.Empty<TeammateStatus>();
        }

        if (!_teamMemberDetails.TryGetValue(teamId, out var memberDetails))
        {
            return Array.Empty<TeammateStatus>();
        }

        var runningTeammates = ResolvedTeammateObserver is not null
            ? await ResolvedTeammateObserver.GetRunningTeammatesAsync().ConfigureAwait(false)
            : [];
        var runningMap = runningTeammates.ToDictionary(t => t.Id);

        var statuses = memberDetails.Values
            .Select(md => BuildTeammateStatus(md, team, runningMap))
            .ToList();

        return statuses;
    }

    public async Task<IReadOnlyList<TeammateStatus>> GetAllTeammateStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        var runningTeammates = ResolvedTeammateObserver is not null
            ? await ResolvedTeammateObserver.GetRunningTeammatesAsync().ConfigureAwait(false)
            : [];
        var runningMap = runningTeammates.ToDictionary(t => t.Id);

        var statuses = new List<TeammateStatus>();

        foreach (var kvp in _teams)
        {
            var team = kvp.Value;
            if (!_teamMemberDetails.TryGetValue(team.TeamId, out var memberDetails)) continue;

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

    public void Dispose() => _lock.Dispose();
}
