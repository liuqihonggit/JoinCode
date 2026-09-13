
namespace JoinCode.Abstractions.Interfaces;

public interface ITeammateObserver
{
    Task<IReadOnlyList<TeammateInfo>> GetRunningTeammatesAsync();

    event EventHandler<TeammateChangedEventArgs>? TeammateChanged;
}

public sealed record TeammateInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string SpinnerVerb { get; init; }
    public required string ColorHex { get; init; }
    public required AgentStatus State { get; init; }
    public DateTime? StartedAt { get; init; }
    public long TokenCount { get; init; }
    public int ToolUseCount { get; init; }
    public string? LastActivity { get; init; }
    public IReadOnlyList<ToolActivity>? RecentActivities { get; init; }
    public string? PastTenseVerb { get; init; }
    public bool ShutdownRequested { get; init; }
    public bool AwaitingPlanApproval { get; init; }
    public string? Prompt { get; init; }
    public IReadOnlyList<string> PreviewLines { get; init; } = [];
    public DateTime? IdleStartedAt { get; init; }
}

public sealed class TeammateChangedEventArgs : EventArgs
{
    public required string AgentId { get; init; }
    public required AgentStatus OldState { get; init; }
    public required AgentStatus NewState { get; init; }
}
