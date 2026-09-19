namespace Core.Agents.Coordinator;

/// <summary>队友重连服务 — 监控断连队友并按指数退避策略自动重连，最大重试次数与退避上限可配置</summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.ITeammateReconnectService), ServiceLifetime.Singleton)]
public sealed partial class TeammateReconnectService : ServiceEntity, JoinCode.Abstractions.Interfaces.ITeammateReconnectService {
    private const int MaxReconnectAttempts = 5;
    private const int InitialBackoffMs = 2000;
    private const int MaxBackoffMs = 300000;

    private readonly ITeamManager _teamManager;
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly ILogger<TeammateReconnectService>? _logger;
    private readonly ConcurrentDictionary<string, int> _reconnectAttempts = new(StringComparer.Ordinal);

    /// <summary>
    /// 初始化 Teammate 重连服务
    /// </summary>
    /// <param name="teamManager">团队管理器</param>
    /// <param name="lifecycleManager">智能体生命周期管理器</param>
    /// <param name="logger">日志记录器</param>
    public TeammateReconnectService(
        ITeamManager teamManager,
        IAgentLifecycleManager lifecycleManager,
        ILogger<TeammateReconnectService>? logger = null) {
        _teamManager = teamManager ?? throw new ArgumentNullException(nameof(teamManager));
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _logger = logger;
    }

    /// <summary>
    /// 异步从持久化状态恢复团队上下文
    /// </summary>
    /// <param name="teamName">团队名称</param>
    /// <param name="agentName">当前智能体名称（Leader 为 null）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>恢复的团队上下文，团队不存在则返回 null</returns>
    public async Task<JoinCode.Abstractions.Interfaces.TeamContext?> RestoreTeamContextAsync(
        string teamName, string? agentName = null, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamName);

        var teams = await _teamManager.ListTeamsAsync(cancellationToken).ConfigureAwait(false);
        var team = teams.FirstOrDefault(t => string.Equals(t.TeamName, teamName, StringComparison.OrdinalIgnoreCase));
        if (team is null) {
            _logger?.LogWarning("Team '{TeamName}' not found for context restoration", teamName);
            return null;
        }

        var members = await _teamManager.GetTeammateStatusesAsync(team.TeamId, cancellationToken).ConfigureAwait(false);
        var teammates = new Dictionary<string, JoinCode.Abstractions.Interfaces.ReconnectTeammateEntry>(StringComparer.Ordinal);

        foreach (var member in members) {
            if (string.IsNullOrEmpty(member.AgentId)) continue;
            teammates[member.AgentId] = new JoinCode.Abstractions.Interfaces.ReconnectTeammateEntry {
                AgentId = member.AgentId,
                Name = member.DisplayName ?? member.AgentId,
                Color = member.ColorHex,
                IsActive = member.IsActive,
                Mode = member.PermissionMode,
                SessionId = member.AgentId,
                WorktreePath = member.WorktreePath
            };
        }

        var selfAgentId = members.FirstOrDefault(m =>
            string.Equals(m.DisplayName, agentName, StringComparison.OrdinalIgnoreCase))?.AgentId;

        return new JoinCode.Abstractions.Interfaces.TeamContext {
            TeamName = teamName,
            TeamId = team.TeamId,
            LeadAgentId = team.LeadAgentId,
            SelfAgentId = selfAgentId,
            SelfAgentName = agentName,
            IsLeader = string.IsNullOrEmpty(agentName),
            Teammates = teammates
        };
    }

    /// <summary>
    /// 异步从会话转录恢复团队上下文（暂未实现，返回 null）
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>团队上下文（当前实现始终返回 null）</returns>
    public Task<JoinCode.Abstractions.Interfaces.TeamContext?> RestoreFromTranscriptAsync(
        string sessionId, CancellationToken cancellationToken = default) {
        _logger?.LogDebug("Transcript-based context restoration not yet implemented for session {SessionId}", sessionId);
        return Task.FromResult<JoinCode.Abstractions.Interfaces.TeamContext?>(null);
    }

    /// <summary>
    /// 异步重连指定 Teammate，使用指数退避策略，超过最大重试次数则放弃
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="agentId">智能体标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重连结果</returns>
    public async Task<JoinCode.Abstractions.Interfaces.ReconnectResult> ReconnectTeammateAsync(
        string teamId, string agentId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var attemptKey = $"{teamId}:{agentId}";
        var attempt = _reconnectAttempts.AddOrUpdate(attemptKey, 1, (_, v) => v + 1);

        if (attempt > MaxReconnectAttempts) {
            _reconnectAttempts.TryRemove(attemptKey, out _);
            _logger?.LogWarning("Max reconnect attempts ({Max}) exceeded for agent {AgentId} in team {TeamId}",
                MaxReconnectAttempts, agentId, teamId);

            return new JoinCode.Abstractions.Interfaces.ReconnectResult {
                AgentId = agentId,
                Status = JoinCode.Abstractions.Interfaces.ReconnectStatus.MaxRetriesExceeded,
                AttemptCount = attempt
            };
        }

        try {
            var backoff = new ExponentialBackoff(
                TimeSpan.FromMilliseconds(InitialBackoffMs),
                TimeSpan.FromMilliseconds(MaxBackoffMs));
            var delay = backoff.CalculateDelay(attempt - 1);
            _logger?.LogInformation("Reconnect attempt {Attempt}/{Max} for agent {AgentId}, backoff {BackoffMs}ms",
                attempt, MaxReconnectAttempts, agentId, delay.TotalMilliseconds);

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            var team = await _teamManager.GetTeamAsync(teamId, cancellationToken).ConfigureAwait(false);
            if (team is null) {
                return new JoinCode.Abstractions.Interfaces.ReconnectResult {
                    AgentId = agentId,
                    Status = JoinCode.Abstractions.Interfaces.ReconnectStatus.Failed,
                    AttemptCount = attempt,
                    ErrorMessage = $"Team '{teamId}' not found"
                };
            }

            await _teamManager.SetMemberActiveAsync(teamId, agentId, true, cancellationToken).ConfigureAwait(false);

            _reconnectAttempts.TryRemove(attemptKey, out _);

            _logger?.LogInformation("Teammate {AgentId} reconnected successfully on attempt {Attempt}",
                agentId, attempt);

            return new JoinCode.Abstractions.Interfaces.ReconnectResult {
                AgentId = agentId,
                Status = JoinCode.Abstractions.Interfaces.ReconnectStatus.Success,
                AttemptCount = attempt
            };
        } catch (OperationCanceledException) {
            _reconnectAttempts.TryRemove(attemptKey, out _);
            return new JoinCode.Abstractions.Interfaces.ReconnectResult {
                AgentId = agentId,
                Status = JoinCode.Abstractions.Interfaces.ReconnectStatus.Cancelled,
                AttemptCount = attempt
            };
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Reconnect attempt {Attempt} failed for agent {AgentId}",
                attempt, agentId);

            return new JoinCode.Abstractions.Interfaces.ReconnectResult {
                AgentId = agentId,
                Status = JoinCode.Abstractions.Interfaces.ReconnectStatus.Failed,
                AttemptCount = attempt,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 异步重连团队中所有已断开的 Teammate，返回最差状态聚合结果
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聚合重连结果</returns>
    public async Task<JoinCode.Abstractions.Interfaces.ReconnectResult> ReconnectAllDisconnectedAsync(
        string teamId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);

        var statuses = await _teamManager.GetTeammateStatusesAsync(teamId, cancellationToken).ConfigureAwait(false);
        var disconnected = statuses.Where(s => !s.IsActive).ToList();

        if (disconnected.Count == 0) {
            _logger?.LogDebug("No disconnected teammates in team {TeamId}", teamId);
            return new JoinCode.Abstractions.Interfaces.ReconnectResult {
                AgentId = "all",
                Status = JoinCode.Abstractions.Interfaces.ReconnectStatus.Success,
                AttemptCount = 0
            };
        }

        JoinCode.Abstractions.Interfaces.ReconnectStatus worstStatus = JoinCode.Abstractions.Interfaces.ReconnectStatus.Success;
        var totalAttempts = 0;

        foreach (var teammate in disconnected) {
            if (cancellationToken.IsCancellationRequested) break;

            var result = await ReconnectTeammateAsync(teamId, teammate.AgentId, cancellationToken).ConfigureAwait(false);
            totalAttempts += result.AttemptCount;

            if (result.Status > worstStatus)
                worstStatus = result.Status;
        }

        return new JoinCode.Abstractions.Interfaces.ReconnectResult {
            AgentId = "all",
            Status = worstStatus,
            AttemptCount = totalAttempts
        };
    }
}