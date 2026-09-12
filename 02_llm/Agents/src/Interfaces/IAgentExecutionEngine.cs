
namespace Core.Agents.Interfaces;

/// <summary>
/// Agent 执行引擎接口 - 负责执行策略（并行/串行）
/// </summary>
public interface IAgentExecutionEngine
{
    /// <summary>
    /// 并行执行多个Agent
    /// </summary>
    Task<IReadOnlyList<SubAgentResult>> ExecuteParallelAsync(
        IEnumerable<IAgent> agents,
        ParallelOptions? options = null,
        ClusterExecutionOptions? clusterOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 串行执行多个Agent（结果传递）
    /// </summary>
    Task<IReadOnlyList<SubAgentResult>> ExecuteSequentialAsync(
        IEnumerable<IAgent> agents,
        CancellationToken cancellationToken = default);
}
