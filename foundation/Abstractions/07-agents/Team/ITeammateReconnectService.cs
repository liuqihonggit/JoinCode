namespace JoinCode.Abstractions.Interfaces;

public interface ITeammateReconnectService
{
    Task<TeamContext?> RestoreTeamContextAsync(string teamName, string? agentName = null, CancellationToken cancellationToken = default);

    Task<TeamContext?> RestoreFromTranscriptAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<ReconnectResult> ReconnectTeammateAsync(string teamId, string agentId, CancellationToken cancellationToken = default);

    Task<ReconnectResult> ReconnectAllDisconnectedAsync(string teamId, CancellationToken cancellationToken = default);
}

public sealed class TeamContext
{
    public required string TeamName { get; init; }
    public required string TeamId { get; init; }
    public string? LeadAgentId { get; init; }
    public string? SelfAgentId { get; init; }
    public string? SelfAgentName { get; init; }
    public bool IsLeader { get; init; }
    public Dictionary<string, ReconnectTeammateEntry> Teammates { get; init; } = new(StringComparer.Ordinal);
}

public sealed class ReconnectTeammateEntry
{
    public required string AgentId { get; init; }
    public required string Name { get; init; }
    public string? Color { get; init; }
    public bool IsActive { get; init; } = true;
    public string? Mode { get; init; }
    public string? SessionId { get; init; }
    public string? WorktreePath { get; init; }
}

public sealed class ReconnectResult
{
    public required string AgentId { get; init; }
    public required ReconnectStatus Status { get; init; }
    public int AttemptCount { get; init; }
    public string? ErrorMessage { get; init; }
}

public enum ReconnectStatus
{
    Success,
    Failed,
    MaxRetriesExceeded,
    Cancelled
}
