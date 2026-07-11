namespace JoinCode.Abstractions.Interfaces;

public interface IAgentLifecycleManager
{
    Task<ISubAgent> SpawnSubAgentAsync(string task, SubAgentOptions? options = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ISubAgent>> SpawnSubAgentsAsync(IEnumerable<string> tasks, SubAgentOptions? options = null, CancellationToken cancellationToken = default);
    Task<SubAgentResult> ExecuteAsync(ISubAgent agent, CancellationToken cancellationToken = default);
    Task<bool> PauseAgentAsync(string agentId, CancellationToken ct = default);
    Task<bool> ResumeAgentAsync(string agentId, CancellationToken ct = default);
    Task<bool> CancelAgentAsync(string agentId, CancellationToken ct = default);
    Task CancelAllAsync(CancellationToken ct = default);
    Task<SubAgentResult?> RetryAsync(string agentId, CancellationToken cancellationToken = default);
    Task DisposeAgentAsync(string agentId, CancellationToken cancellationToken = default);
    Task<ISubAgent?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ISubAgent>> GetAllAgentsAsync(CancellationToken cancellationToken = default);
    Task<SubAgentResult?> GetResultAsync(string agentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, SubAgentResult>> GetAllResultsAsync(CancellationToken cancellationToken = default);
    Task WaitAllAsync(CancellationToken cancellationToken = default);
    Task<AgentStateReport> GetStateReportAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunningAgentInfo>> GetRunningAgentsAsync(CancellationToken cancellationToken = default);
}
