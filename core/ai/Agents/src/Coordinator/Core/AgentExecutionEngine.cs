
namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 执行引擎 - 负责执行策略（并行/串行）
/// </summary>
[Register]
public sealed partial class AgentExecutionEngine : IAgentExecutionEngine
{
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly ILogger? _logger;

    public AgentExecutionEngine(IAgentLifecycleManager lifecycleManager, ILogger? logger = null)
    {
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
        CancellationToken cancellationToken = default)
    {
        options ??= new ParallelOptions { MaxDegreeOfParallelism = CpuParallelism.GetDegree() };
        var maxConcurrency = clusterOptions?.MaxConcurrency ?? options.MaxDegreeOfParallelism;

        var agentList = agents.ToList();
        var semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrency), Math.Max(1, maxConcurrency));

        var tasks = agentList
            .Select(async agent =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var result = await _lifecycleManager.ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);
                    return (AgentId: agent.ObjectId.UniqueId, Result: result);
                }
                finally
                {
                    semaphore.Release();
                }
            })
            .ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var resultDict = results.ToDictionary(r => r.AgentId, r => r.Result);

        return agentList
            .Select(a => resultDict[a.ObjectId.UniqueId])
            .ToList();
    }

    /// <summary>
    /// 串行执行多个Agent（结果传递）
    /// </summary>
    public async Task<IReadOnlyList<SubAgentResult>> ExecuteSequentialAsync(
        IEnumerable<IAgent> agents,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SubAgentResult>();
        string? previousResult = null;

        foreach (var agent in agents)
        {
            // 添加上下文
            if (previousResult != null)
            {
                ((Agent)agent).AddContext($"上一个任务的结果: {previousResult}");
            }

            var result = await _lifecycleManager.ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (!result.IsSuccess)
            {
                _logger?.LogWarning("[AgentExecutionEngine] Agent {AgentId} 执行失败，停止序列执行", agent.ObjectId.UniqueId);
                break;
            }

            previousResult = result.Output;
        }

        return results;
    }
}
