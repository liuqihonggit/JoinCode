
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
    /// 并行执行多个Agent
    /// </summary>
    public async Task<IReadOnlyList<SubAgentResult>> ExecuteParallelAsync(
        IEnumerable<ISubAgent> agents,
        ParallelOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ParallelOptions { MaxDegreeOfParallelism = CpuParallelism.GetDegree() };

        var agentList = agents.ToList();

        // 使用 LINQ 和 Task.WhenAll 替代 Parallel.ForEachAsync，符合项目规范
        var tasks = agentList
            .Select(async agent =>
            {
                var result = await _lifecycleManager.ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);
                return (AgentId: agent.Id, Result: result);
            })
            .ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // 使用字典保持原始顺序
        var resultDict = results.ToDictionary(r => r.AgentId, r => r.Result);

        return agentList
            .Select(a => resultDict[a.Id])
            .ToList();
    }

    /// <summary>
    /// 串行执行多个Agent（结果传递）
    /// </summary>
    public async Task<IReadOnlyList<SubAgentResult>> ExecuteSequentialAsync(
        IEnumerable<ISubAgent> agents,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SubAgentResult>();
        string? previousResult = null;

        foreach (var agent in agents)
        {
            // 添加上下文
            if (previousResult != null)
            {
                agent.AddContext($"上一个任务的结果: {previousResult}");
            }

            var result = await _lifecycleManager.ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (!result.IsSuccess)
            {
                _logger?.LogWarning("[AgentExecutionEngine] Agent {AgentId} 执行失败，停止序列执行", agent.Id);
                break;
            }

            previousResult = result.Output;
        }

        return results;
    }
}
