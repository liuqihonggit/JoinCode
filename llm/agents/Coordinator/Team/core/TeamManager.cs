
namespace Core.Agents.Coordinator;

/// <summary>
/// 团队管理器实现
/// 使用单一锁保护成员和消息操作，消除多锁排序风险
/// </summary>
[Register(typeof(ITeamManager), ServiceLifetime.Singleton)]
public sealed partial class TeamManager : ServiceEntity, ITeamManager, IDisposable
{
    private readonly TeamRegistry _registry = new();
    private readonly AsyncLock _lock = new();
    private readonly TeamMessageDispatcher _messageDispatcher;
    private readonly TeammateStatusBuilder _teammateStatusBuilder;
    private readonly ChatRoomViewBuilder _chatRoomViewBuilder;
    private readonly ITelemetryService? _telemetryService;
    private readonly ITeammateMailboxService? _mailboxService;
    private readonly MailboxHub? _mailboxHub;
    private readonly IServiceProvider? _serviceProvider;
    private readonly IClockService _clock;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ILogger<TeamManager>? _logger;
    private int _teamCounter;
    private int _messageCounter;
    private bool _disposed;

    /// <summary>
    /// 延迟解析 ITeammateObserver，打破循环依赖：
    /// AgentCoordinator → ITeammateReconnectService → ITeamManager → ITeammateObserver → AgentCoordinator
    /// </summary>
    private ITeammateObserver? ResolvedTeammateObserver =>
        _serviceProvider?.GetService(typeof(ITeammateObserver)) as ITeammateObserver;

    /// <summary>
    /// 初始化团队管理器，可选加载持久化状态
    /// </summary>
    /// <param name="clock">时钟服务</param>
    /// <param name="telemetryService">遥测服务</param>
    /// <param name="mailboxService">邮箱服务（文件通道遗留路由）</param>
    /// <param name="mailboxHub">邮箱中枢（跨通道广播 NamedPipe/Network — ADR 0111）</param>
    /// <param name="serviceProvider">服务提供者（用于延迟解析 ITeammateObserver）</param>
    /// <param name="subAgentContextAccessor">子智能体上下文访问器</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="fileSystem">文件系统（提供则启用持久化）</param>
    public TeamManager(IClockService clock, ITelemetryService? telemetryService = null, ITeammateMailboxService? mailboxService = null, MailboxHub? mailboxHub = null, IServiceProvider? serviceProvider = null, ISubAgentContextAccessor? subAgentContextAccessor = null, ILogger<TeamManager>? logger = null, IFileSystem? fileSystem = null)
    {

        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _telemetryService = telemetryService;
        _mailboxService = mailboxService;
        _mailboxHub = mailboxHub;
        _serviceProvider = serviceProvider;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _logger = logger;
        _messageDispatcher = new TeamMessageDispatcher(_registry, mailboxService, mailboxHub, _logger);
        _teammateStatusBuilder = new TeammateStatusBuilder(_registry, () => ResolvedTeammateObserver);
        _chatRoomViewBuilder = new ChatRoomViewBuilder(_registry, _teammateStatusBuilder.GetTeammateStatusesAsync);
        _persistenceFs = fileSystem;
        _stateFilePath = fileSystem is not null ? GetStateFilePath() : null;
        LoadState();
    }

    /// <summary>
    /// 异步创建团队，校验名称唯一性与单团队限制后写入状态
    /// </summary>
    /// <param name="teamName">团队名称</param>
    /// <param name="description">团队描述</param>
    /// <param name="initialMembers">初始成员列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建结果（成功包含团队信息，失败包含错误消息）</returns>
    public async Task<OperationResult<TeamInfo?>> CreateTeamAsync(
        string teamName,
        string? description = null,
        List<string>? initialMembers = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamName))
        {
            return OperationResult<TeamInfo?>.Fail("团队名称不能为空");
        }

        // 单团队限制：当前会话已存在团队时不允许再创建（对齐 TS TeamCreateTool）
        var sessionId = _subAgentContextAccessor.Current?.SessionId;
        if (sessionId is not null)
        {
            var existingRoomForSession = _registry.FindRoomBySessionId(sessionId);
            if (existingRoomForSession is not null)
            {
                return OperationResult<TeamInfo?>.Fail("已在团队中，请先使用 TeamDelete 删除当前团队");
            }
        }

        // 名称唯一性检查（对齐 TS generateUniqueTeamName）
        var nameConflict = _registry.FindTeamByName(teamName);
        if (nameConflict is not null)
        {
            return OperationResult<TeamInfo?>.Fail($"团队名称 '{teamName}' 已存在，请使用其他名称");
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

        var room = new ChatRoomState
        {
            Info = team,
            Members = members,
            MemberDetails = memberDetails,
        };

        if (sessionId is not null)
        {
            room.SessionId = sessionId;
        }

        _registry.AddRoom(teamId, room);

        if (initialMembers != null)
        {
            foreach (var member in initialMembers)
            {
                _registry.RegisterAgentToTeam(member, teamId);
            }
        }

        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<TeamInfo?>.Ok(team);
    }

    /// <summary>
    /// 异步删除团队，要求无活跃成员才能删除
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除结果（成功包含已删除团队信息）</returns>
    public async Task<OperationResult<TeamInfo?>> DeleteTeamAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            RecordTeamMetrics("delete", false);
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var team = room.Info;

        // Active member 安全检查（对齐 TS TeamDeleteTool）
        {
            var activeMembers = room.MemberDetails.Values.Where(m => m.IsActive).ToList();
            if (activeMembers.Count > 0)
            {
                var activeNames = string.Join(", ", activeMembers.Select(m => m.AgentId));
                return OperationResult<TeamInfo?>.Fail($"团队仍有活跃成员: {activeNames}，请先优雅关闭所有队友再删除团队");
            }
        }

        _registry.TryRemoveRoom(teamId, out _);

        RecordTeamMetrics("delete", true);
        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<TeamInfo?>.Ok(team);
    }

    /// <summary>
    /// 异步获取指定团队信息
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>团队信息，不存在则返回 null</returns>
    public Task<TeamInfo?> GetTeamAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        _registry.TryGetRoom(teamId, out var room);
        return Task.FromResult(room?.Info);
    }

    /// <summary>
    /// 异步列出所有团队
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>团队信息只读列表</returns>
    public Task<IReadOnlyList<TeamInfo>> ListTeamsAsync(
        CancellationToken cancellationToken = default)
    {
        var teams = _registry.Rooms.Select(r => r.Info).ToList();
        return Task.FromResult<IReadOnlyList<TeamInfo>>(teams);
    }

    /// <summary>
    /// 异步向团队添加成员
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="agentId">智能体标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>添加结果（成功包含更新后的团队信息）</returns>
    public async Task<OperationResult<TeamInfo?>> AddTeamMemberAsync(
        string teamId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var members = room.Members;
        var memberDetails = room.MemberDetails;

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        if (members.Contains(agentId))
        {
            return OperationResult<TeamInfo?>.Fail($"代理 {agentId} 已经是团队成员");
        }

        members.Add(agentId);
        memberDetails[agentId] = new TeamMemberInfo { AgentId = agentId, JoinedAt = _clock.GetUtcNow() };

        UpdateRoomMembers(room);

        _registry.RegisterAgentToTeam(agentId, teamId);

        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<TeamInfo?>.Ok(room.Info);
    }

    /// <summary>
    /// 异步从团队移除成员
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="agentId">智能体标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>移除结果（成功包含更新后的团队信息）</returns>
    public async Task<OperationResult<TeamInfo?>> RemoveTeamMemberAsync(
        string teamId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var members = room.Members;
        var memberDetails = room.MemberDetails;

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        if (!members.Remove(agentId))
        {
            return OperationResult<TeamInfo?>.Fail($"代理 {agentId} 不是团队成员");
        }

        memberDetails.Remove(agentId);

        _registry.UnregisterAgentFromTeam(agentId);

        UpdateRoomMembers(room);

        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<TeamInfo?>.Ok(room.Info);
    }

    /// <summary>
    /// 异步获取团队成员列表
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成员标识只读列表</returns>
    public Task<IReadOnlyList<string>> GetTeamMembersAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        return Task.FromResult<IReadOnlyList<string>>(room.Members.ToList());
    }

    /// <summary>
    /// 异步向团队发送消息，要求发送者是团队成员
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="senderId">发送者标识</param>
    /// <param name="content">消息内容</param>
    /// <param name="messageType">消息类型（默认 broadcast）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>发送结果（成功包含更新后的团队信息）</returns>
    public async Task<OperationResult<TeamInfo?>> SendMessageAsync(
        string teamId,
        string senderId,
        string content,
        string? messageType = null,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var team = room.Info;

        var message = new TeamMessage
        {
            MessageId = GenerateMessageId(),
            TeamId = teamId,
            SenderId = senderId,
            Content = content,
            MessageType = messageType ?? "broadcast",
            Timestamp = _clock.GetUtcNow()
        };

        var messages = room.Messages;

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        if (!room.Members.Contains(senderId))
        {
            return OperationResult<TeamInfo?>.Fail($"发送者 {senderId} 不是团队成员");
        }

        if (!messages.TryAdd(message.MessageId, message))
        {
            _logger?.LogDebug("Duplicate team message {MessageId} skipped in SendMessageAsync", message.MessageId);
            return OperationResult<TeamInfo?>.Ok(room.Info);
        }
    

        TouchRoomActivity(room);

        await _messageDispatcher.PersistTeamMessageToMailboxAsync(teamId, message, cancellationToken).ConfigureAwait(false);

        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<TeamInfo?>.Ok(room.Info);
    }

    /// <summary>
    /// 异步向团队中指定智能体发送私信
    /// </summary>
    /// <param name="targetAgentId">目标智能体标识</param>
    /// <param name="senderId">发送者标识</param>
    /// <param name="content">消息内容</param>
    /// <param name="messageType">消息类型（默认 direct）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>发送结果（成功包含团队信息）</returns>
    public async Task<OperationResult<TeamInfo?>> SendMessageToAgentAsync(
        string targetAgentId,
        string senderId,
        string content,
        string? messageType = null,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetTeamIdForAgent(targetAgentId, out var teamId))
        {
            return OperationResult<TeamInfo?>.Fail($"代理 {targetAgentId} 不属于任何团队");
        }

        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var team = room.Info;

        var message = new TeamMessage
        {
            MessageId = GenerateMessageId(),
            TeamId = teamId,
            SenderId = senderId,
            Content = $"[私信给 {targetAgentId}] {content}",
            MessageType = messageType ?? "direct",
            Timestamp = _clock.GetUtcNow()
        };

        var messages = room.Messages;

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        if (!room.Members.Contains(senderId))
        {
            return OperationResult<TeamInfo?>.Fail($"发送者 {senderId} 不是团队成员");
        }

        if (!messages.TryAdd(message.MessageId, message))
        {
            _logger?.LogDebug("Duplicate team message {MessageId} skipped in SendMessageToAgentAsync", message.MessageId);
            return OperationResult<TeamInfo?>.Ok(team);
        }
    

        await _messageDispatcher.PersistDirectMessageToMailboxAsync(targetAgentId, senderId, content, messageType ?? "direct", teamId, cancellationToken).ConfigureAwait(false);

        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<TeamInfo?>.Ok(team);
    }

    /// <summary>
    /// 异步获取团队消息列表，按时间倒序返回指定条数
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="limit">返回消息上限（默认 50）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>团队消息只读列表</returns>
    public async Task<IReadOnlyList<TeamMessage>> GetTeamMessagesAsync(
        string teamId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return Array.Empty<TeamMessage>();
        }

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        return room.GetMessages(limit);
    }

    /// <summary>
    /// 异步广播消息到团队所有成员，要求发送者是团队成员
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="senderId">发送者标识</param>
    /// <param name="content">消息内容</param>
    /// <param name="messageType">消息类型（默认 broadcast）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>广播结果（成功包含更新后的团队信息）</returns>
    public async Task<OperationResult<TeamInfo?>> BroadcastMessageAsync(
        string teamId,
        string senderId,
        string content,
        string? messageType = null,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var team = room.Info;

        var message = new TeamMessage
        {
            MessageId = GenerateMessageId(),
            TeamId = teamId,
            SenderId = senderId,
            Content = $"[广播] {content}",
            MessageType = messageType ?? "broadcast",
            Timestamp = _clock.GetUtcNow()
        };

        var messages = room.Messages;

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        if (!room.Members.Contains(senderId))
        {
            return OperationResult<TeamInfo?>.Fail($"发送者 {senderId} 不是团队成员");
        }

        if (!messages.TryAdd(message.MessageId, message))
        {
            _logger?.LogDebug("Duplicate team message {MessageId} skipped in BroadcastMessageAsync", message.MessageId);
            return OperationResult<TeamInfo?>.Ok(room.Info);
        }
    

        TouchRoomActivity(room);

        await _messageDispatcher.PersistTeamMessageToMailboxAsync(teamId, message, cancellationToken).ConfigureAwait(false);

        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<TeamInfo?>.Ok(room.Info);
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
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "team.operation.count", operation, isSuccess, "Team operation count");

    /// <summary>
    /// 更新房间最后活动时间。
    /// </summary>
    private void TouchRoomActivity(ChatRoomState room)
        => room.Info = room.Info with { LastActivityAt = _clock.GetUtcNow() };

    /// <summary>
    /// 更新房间成员信息(成员列表+成员详情+最后活动时间)。
    /// </summary>
    private void UpdateRoomMembers(ChatRoomState room)
    {
        room.Info = room.Info with
        {
            Members = room.Members.ToList(),
            MemberDetails = room.MemberDetails.Values.ToList(),
            LastActivityAt = _clock.GetUtcNow()
        };
    }

    /// <summary>
    /// 异步设置团队成员的活跃状态
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="agentId">智能体标识</param>
    /// <param name="isActive">是否活跃</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设置结果（成功包含更新后的团队信息）</returns>
    public async Task<OperationResult<TeamInfo?>> SetMemberActiveAsync(
        string teamId,
        string agentId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var team = room.Info;
        var memberDetails = room.MemberDetails;

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        if (!memberDetails.TryGetValue(agentId, out var existing))
        {
            return OperationResult<TeamInfo?>.Fail($"代理 {agentId} 不是团队成员");
        }

        memberDetails[agentId] = existing with { IsActive = isActive };
    

        room.Info = team with
        {
            MemberDetails = memberDetails.Values.ToList(),
            LastActivityAt = _clock.GetUtcNow()
        };

        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<TeamInfo?>.Ok(room.Info);
    }

    /// <summary>
    /// 异步获取团队允许的路径列表
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>允许路径只读列表</returns>
    public Task<IReadOnlyList<TeamAllowedPath>> GetTeamAllowedPathsAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return Task.FromResult<IReadOnlyList<TeamAllowedPath>>(Array.Empty<TeamAllowedPath>());
        }

        return Task.FromResult<IReadOnlyList<TeamAllowedPath>>(room.AllowedPaths.Values.ToList());
    }

    /// <summary>
    /// 异步向团队添加允许路径，已存在则更新访问级别
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="path">路径</param>
    /// <param name="accessLevel">访问级别（默认 Read）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>添加结果（成功包含更新后的团队信息）</returns>
    public async Task<OperationResult<TeamInfo?>> AddTeamAllowedPathAsync(
        string teamId,
        string path,
        AccessLevel accessLevel = AccessLevel.Read,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var team = room.Info;

        if (string.IsNullOrWhiteSpace(path))
        {
            return OperationResult<TeamInfo?>.Fail("路径不能为空");
        }

        var paths = room.AllowedPaths;

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        if (paths.TryGetValue(path, out var existing))
        {
            paths[path] = existing with { AccessLevel = accessLevel };
        }
        else
        {
            paths[path] = new TeamAllowedPath { Path = path, AccessLevel = accessLevel };
        }
    

        room.Info = team with
        {
            AllowedPaths = paths.Values.ToList(),
            LastActivityAt = _clock.GetUtcNow()
        };

        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<TeamInfo?>.Ok(room.Info);
    }

    /// <summary>
    /// 异步获取指定团队所有 Teammate 的状态 — 委托给 TeammateStatusBuilder
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Teammate 状态只读列表</returns>
    public Task<IReadOnlyList<TeammateStatus>> GetTeammateStatusesAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        return _teammateStatusBuilder.GetTeammateStatusesAsync(teamId, cancellationToken);
    }

    /// <summary>
    /// 异步获取所有团队的所有 Teammate 状态 — 委托给 TeammateStatusBuilder
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>所有 Teammate 状态只读列表</returns>
    public Task<IReadOnlyList<TeammateStatus>> GetAllTeammateStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        return _teammateStatusBuilder.GetAllTeammateStatusesAsync(cancellationToken);
    }

    /// <summary>
    /// 获取聊天室信息 — 委托给 ChatRoomViewBuilder
    /// </summary>
    public Task<ChatRoomInfo?> GetChatRoomInfoAsync(
        string teamId,
        CancellationToken cancellationToken = default)
    {
        return _chatRoomViewBuilder.GetChatRoomInfoAsync(teamId, cancellationToken);
    }

    /// <summary>
    /// 撤回团队消息 — 对标 QQ 消息撤回（2 分钟内可撤回）— ADR 0109 决策11。
    /// </summary>
    public async Task<OperationResult<TeamInfo?>> RevokeMessageAsync(
        string teamId,
        string messageId,
        string revokerId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetRoom(teamId, out var room))
        {
            return OperationResult<TeamInfo?>.Fail($"团队 {teamId} 不存在");
        }

        var team = room.Info;
        var msgDict = room.Messages;

        if (!msgDict.TryGetValue(messageId, out var originalMsg))
        {
            return OperationResult<TeamInfo?>.Fail($"消息 {messageId} 不存在");
        }

        var isSender = originalMsg.SenderId == revokerId;
        var isAdmin = TeamMessageDispatcher.IsAdminOrOwner(revokerId, room.MemberDetails, team);
        if (!isSender && !isAdmin)
        {
            return OperationResult<TeamInfo?>.Fail($"撤回者 {revokerId} 无权限：仅发送者或管理员可撤回");
        }

        var revokeTimeLimit = TimeSpan.FromMinutes(2);
        if (!isAdmin && _clock.GetUtcNow() - originalMsg.Timestamp > revokeTimeLimit)
        {
            return OperationResult<TeamInfo?>.Fail("消息发送超过 2 分钟，非管理员无法撤回");
        }

        var revokedMsg = originalMsg with
        {
            Visibility = MessageVisibility.Hidden,
            RevokeReason = reason ?? "撤回",
        };
        msgDict[messageId] = revokedMsg;

        var notice = SystemNoticeFactory.Create(SystemNoticeKind.MessageRevoked, teamId, revokerId);
        if (msgDict.TryAdd(notice.MessageId, notice))
        {
            await _messageDispatcher.PersistTeamMessageToMailboxAsync(teamId, notice, cancellationToken).ConfigureAwait(false);
        }

        TouchRoomActivity(room);
        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<TeamInfo?>.Ok(room.Info);
    }

    /// <summary>释放资源 — 释放团队管理锁</summary>
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
        base.Dispose();
    }
}
