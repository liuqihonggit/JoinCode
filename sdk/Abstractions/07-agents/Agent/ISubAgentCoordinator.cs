namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 子代理协调器接口 — 用于调度层创建、执行和清理子代理
/// </summary>
public interface ISubAgentCoordinator
{
    /// <summary>
    /// 创建子代理
    /// </summary>
    Task<ISubAgent> SpawnSubAgentAsync(string task, SubAgentOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 并行执行多个子代理
    /// </summary>
    Task<IReadOnlyList<SubAgentResult>> ExecuteParallelAsync(IEnumerable<ISubAgent> agents, ParallelOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 释放子代理资源
    /// </summary>
    Task DisposeAgentAsync(string agentId, CancellationToken cancellationToken = default);
}
