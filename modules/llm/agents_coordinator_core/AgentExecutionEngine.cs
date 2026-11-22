
namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 执行引擎 - 负责执行策略（并行/串行）
/// </summary>
[Register(typeof(IAgentExecutionEngine), ServiceLifetime.Singleton)]
public sealed partial class AgentExecutionEngine : ServiceEntity, IAgentExecutionEngine {
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造 Agent 执行引擎实例
    /// </summary>
    /// <param name="lifecycleManager">Agent 生命周期管理器，用于执行单个 Agent</param>
    /// <param name="logger">可选日志记录器</param>
    public AgentExecutionEngine(IAgentLifecycleManager lifecycleManager, ILogger? logger = null) {
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _logger = logger;
    }

    /// <summary>
    /// 并行执行多个Agent — 支持并发度控制
    /// </summary>
    public async Task<IReadOnlyList<SubAgentResult>> ExecuteParallelAsync(
        IEnumerable<IAgent> agents,
        ParallelOptions? options = null,
        ClusterExecutionOptions? clusterOptions = null,
        CancellationToken cancellationToken = default) {
        options ??= new ParallelOptions { MaxDegreeOfParallelism = CpuParallelism.GetDegree() };
        var maxConcurrency = clusterOptions?.MaxConcurrency ?? options.MaxDegreeOfParallelism;

        var agentList = agents.ToList();
        if (agentList.Count == 0) return [];

        var results = new SubAgentResult[agentList.Count];
        var nextIndex = -1;

        async Task WorkerAsync() {
            while (true) {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= agentList.Count) return;
                results[index] = await _lifecycleManager.ExecuteAsync(agentList[index], cancellationToken).ConfigureAwait(false);
            }
        }

        var workerCount = Math.Min(Math.Max(1, maxConcurrency), agentList.Count);
        await Task.WhenAll(Enumerable.Range(0, workerCount).Select(_ => WorkerAsync())).ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// 串行执行多个Agent（结果传递）
    /// </summary>
    public async Task<IReadOnlyList<SubAgentResult>> ExecuteSequentialAsync(
        IEnumerable<IAgent> agents,
        CancellationToken cancellationToken = default) {
        var results = new List<SubAgentResult>();
        string? previousResult = null;

        foreach (var agent in agents) {
            // 添加上下文
            if (previousResult != null) {
                agent.AddContext($"上一个任务的结果: {previousResult}");
            }

            var result = await _lifecycleManager.ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (!result.IsSuccess) {
                _logger?.LogWarning("[AgentExecutionEngine] Agent {AgentId} 执行失败，停止序列执行", agent.ObjectId.UniqueId);
                break;
            }

            previousResult = result.Output;
        }

        return results;
    }
}