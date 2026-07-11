namespace Core.Agents.Coordinator;

[Register(typeof(JoinCode.Abstractions.Interfaces.ITeammateReconnectService))]
public sealed partial class TeammateReconnectService : JoinCode.Abstractions.Interfaces.ITeammateReconnectService
{
    private const int MaxReconnectAttempts = 5;
    private const int InitialBackoffMs = 1000;
    private const int MaxBackoffMs = 30000;

    private readonly ITeamManager _teamManager;
    private readonly IAgentLifecycleManager _lifecycleManager;
    [Inject] private readonly ILogger<TeammateReconnectService>? _logger;
    private readonly ConcurrentDictionary<string, int> _reconnectAttempts = new(StringComparer.Ordinal);

    public TeammateReconnectService(
        ITeamManager teamManager,
        IAgentLifecycleManager lifecycleManager,
        ILogger<TeammateReconnectService>? logger = null)
    {
        _teamManager = teamManager ?? throw new ArgumentNullException(nameof(teamManager));
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _logger = logger;
    }

    public async Task<JoinCode.Abstractions.Interfaces.TeamContext?> RestoreTeamContextAsync(
        string teamName, string? agentName = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamName);

        var teams = await _teamManager.ListTeamsAsync(cancellationToken).ConfigureAwait(false);
        var team = teams.FirstOrDefault(t => string.Equals(t.TeamName, teamName, StringComparison.OrdinalIgnoreCase));
        if (team is null)
        {
            _logger?.LogWarning("Team '{TeamName}' not found for context restoration", teamName);
            return null;
        }

        var members = await _teamManager.GetTeammateStatusesAsync(team.TeamId, cancellationToken).ConfigureAwait(false);
        var teammates = new Dictionary<string, JoinCode.Abstractions.Interfaces.ReconnectTeammateEntry>(StringComparer.Ordinal);

        foreach (var member in members)
        {
            if (string.IsNullOrEmpty(member.AgentId)) continue;
            teammates[member.AgentId] = new JoinCode.Abstractions.Interfaces.ReconnectTeammateEntry
            {
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

        return new JoinCode.Abstractions.Interfaces.TeamContext
        {
            TeamName = teamName,
            TeamId = team.TeamId,
            LeadAgentId = team.LeadAgentId,
            SelfAgentId = selfAgentId,
            SelfAgentName = agentName,
            IsLeader = string.IsNullOrEmpty(agentName),
            Teammates = teammates
        };
    }

    public Task<JoinCode.Abstractions.Interfaces.TeamContext?> RestoreFromTranscriptAsync(
        string sessionId, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Transcript-based context restoration not yet implemented for session {SessionId}", sessionId);
        return Task.FromResult<JoinCode.Abstractions.Interfaces.TeamContext?>(null);
    }

    public async Task<JoinCode.Abstractions.Interfaces.ReconnectResult> ReconnectTeammateAsync(
        string teamId, string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var attemptKey = $"{teamId}:{agentId}";
        var attempt = _reconnectAttempts.AddOrUpdate(attemptKey, 1, (_, v) => v + 1);

        if (attempt > MaxReconnectAttempts)
        {
            _reconnectAttempts.TryRemove(attemptKey, out _);
            _logger?.LogWarning("Max reconnect attempts ({Max}) exceeded for agent {AgentId} in team {TeamId}",
                MaxReconnectAttempts, agentId, teamId);

            return new JoinCode.Abstractions.Interfaces.ReconnectResult
            {
                AgentId = agentId,
                Status = JoinCode.Abstractions.Interfaces.ReconnectStatus.MaxRetriesExceeded,
                AttemptCount = attempt
            };
        }

        try
        {
            var backoffMs = Math.Min(InitialBackoffMs * (int)Math.Pow(2, attempt - 1), MaxBackoffMs);
            _logger?.LogInformation("Reconnect attempt {Attempt}/{Max} for agent {AgentId}, backoff {BackoffMs}ms",
                attempt, MaxReconnectAttempts, agentId, backoffMs);

            await Task.Delay(backoffMs, cancellationToken).ConfigureAwait(false);

            var team = await _teamManager.GetTeamAsync(teamId, cancellationToken).ConfigureAwait(false);
            if (team is null)
            {
                return new JoinCode.Abstractions.Interfaces.ReconnectResult
                {
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

            return new JoinCode.Abstractions.Interfaces.ReconnectResult
            {
                AgentId = agentId,
                Status = JoinCode.Abstractions.Interfaces.ReconnectStatus.Success,
                AttemptCount = attempt
            };
        }
        catch (OperationCanceledException)
        {
            _reconnectAttempts.TryRemove(attemptKey, out _);
            return new JoinCode.Abstractions.Interfaces.ReconnectResult
            {
                AgentId = agentId,
                Status = JoinCode.Abstractions.Interfaces.ReconnectStatus.Cancelled,
                AttemptCount = attempt
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Reconnect attempt {Attempt} failed for agent {AgentId}",
                attempt, agentId);

            return new JoinCode.Abstractions.Interfaces.ReconnectResult
            {
                AgentId = agentId,
                Status = JoinCode.Abstractions.Interfaces.ReconnectStatus.Failed,
                AttemptCount = attempt,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<JoinCode.Abstractions.Interfaces.ReconnectResult> ReconnectAllDisconnectedAsync(
        string teamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);

        var statuses = await _teamManager.GetTeammateStatusesAsync(teamId, cancellationToken).ConfigureAwait(false);
        var disconnected = statuses.Where(s => !s.IsActive).ToList();

        if (disconnected.Count == 0)
        {
            _logger?.LogDebug("No disconnected teammates in team {TeamId}", teamId);
            return new JoinCode.Abstractions.Interfaces.ReconnectResult
            {
                AgentId = "all",
                Status = JoinCode.Abstractions.Interfaces.ReconnectStatus.Success,
                AttemptCount = 0
            };
        }

        JoinCode.Abstractions.Interfaces.ReconnectStatus worstStatus = JoinCode.Abstractions.Interfaces.ReconnectStatus.Success;
        var totalAttempts = 0;

        foreach (var teammate in disconnected)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var result = await ReconnectTeammateAsync(teamId, teammate.AgentId, cancellationToken).ConfigureAwait(false);
            totalAttempts += result.AttemptCount;

            if (result.Status > worstStatus)
                worstStatus = result.Status;
        }

        return new JoinCode.Abstractions.Interfaces.ReconnectResult
        {
            AgentId = "all",
            Status = worstStatus,
            AttemptCount = totalAttempts
        };
    }
}
