namespace JoinCode.Abstractions.Interfaces;

public interface IAgentLifecycleManager {
    /// <summary>异步生成单个子 Agent。</summary>
    Task<IAgent> SpawnSubAgentAsync(string task, SubAgentOptions? options = null, CancellationToken cancellationToken = default, string? parentSessionId = null);
    /// <summary>异步批量生成子 Agent。</summary>
    Task<IReadOnlyList<IAgent>> SpawnSubAgentsAsync(IEnumerable<string> tasks, SubAgentOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>异步执行指定 Agent。</summary>
    Task<SubAgentResult> ExecuteAsync(IAgent agent, CancellationToken cancellationToken = default);
    /// <summary>异步暂停指定 Agent。</summary>
    Task<bool> PauseAgentAsync(string agentId, CancellationToken ct = default);
    /// <summary>异步恢复指定 Agent。</summary>
    Task<bool> ResumeAgentAsync(string agentId, CancellationToken ct = default);
    /// <summary>异步取消指定 Agent。</summary>
    Task<bool> CancelAgentAsync(string agentId, CancellationToken ct = default);
    /// <summary>异步取消所有 Agent。</summary>
    Task CancelAllAsync(CancellationToken ct = default);
    /// <summary>异步重试指定 Agent。</summary>
    Task<SubAgentResult?> RetryAsync(string agentId, CancellationToken cancellationToken = default);
    /// <summary>异步释放指定 Agent 资源。</summary>
    Task DisposeAgentAsync(string agentId, CancellationToken cancellationToken = default);
    /// <summary>异步获取指定 Agent。</summary>
    Task<IAgent?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default);
    /// <summary>异步获取所有 Agent。</summary>
    Task<IReadOnlyCollection<IAgent>> GetAllAgentsAsync(CancellationToken cancellationToken = default);
    /// <summary>异步获取指定 Agent 的执行结果。</summary>
    Task<SubAgentResult?> GetResultAsync(string agentId, CancellationToken cancellationToken = default);
    /// <summary>异步获取所有 Agent 的执行结果。</summary>
    Task<IReadOnlyDictionary<string, SubAgentResult>> GetAllResultsAsync(CancellationToken cancellationToken = default);
    /// <summary>异步等待所有 Agent 完成。</summary>
    Task WaitAllAsync(CancellationToken cancellationToken = default);
    /// <summary>异步获取 Agent 状态报告。</summary>
    Task<AgentStateReport> GetStateReportAsync(CancellationToken cancellationToken = default);
    /// <summary>异步获取正在运行的 Agent 信息列表。</summary>
    Task<IEnumerable<RunningAgentInfo>> GetRunningAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>按 ID 获取运行中的 Agent — O(1) 字典查找，避免 GetRunningAgentsAsync + FirstOrDefault 的 O(n) 线性检索</summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>运行中的 Agent 信息；不存在或非运行状态返回 null</returns>
    Task<RunningAgentInfo?> GetRunningAgentByIdAsync(string agentId, CancellationToken cancellationToken = default);
}
