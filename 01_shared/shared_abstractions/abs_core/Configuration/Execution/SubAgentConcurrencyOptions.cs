namespace JoinCode.Abstractions.Configuration.Execution;

/// <summary>
/// 子代理并发控制统一配置 — spawn/execute/fork 三阶段差异化上限。
/// 唯一数据源（ADR 0048），替代 MaxConcurrentAgents(死配置)/MaxConcurrentTasks/MaxConcurrency 分散配置。
/// </summary>
public sealed class SubAgentConcurrencyOptions
{
    /// <summary>
    /// spawn 阶段最大并发数（同时创建子代理数，保护 worktree 磁盘资源）。
    /// 按 ADR 0050 在 AgentCoordinator.SpawnSubAgentAsync 施加 SemaphoreSlim。
    /// </summary>
    public int MaxConcurrentSpawns { get; set; } = 16;

    /// <summary>
    /// execute 阶段最大并发数（同时执行子代理数，保护 CPU/内存）。
    /// 消费方：AgentExecutionEngine.ExecuteParallelAsync / TaskExecutor / GoalGraphEngine。
    /// </summary>
    public int MaxConcurrentExecutions { get; set; } = 24;

    /// <summary>
    /// Fork 最大并发数（同时 fork 子代理数，0=不限）。
    /// 按 ADR 0051 在 ForkSubAgentManager.ForkAsync 施加 SemaphoreSlim。
    /// </summary>
    public int MaxConcurrentForks { get; set; } = 12;

    /// <summary>
    /// 校验配置合法性 — 配置加载时调用，非法值抛 ArgumentException。
    /// </summary>
    public void Validate()
    {
        if (MaxConcurrentSpawns < 1)
            throw new ArgumentException("MaxConcurrentSpawns 必须 >= 1", nameof(MaxConcurrentSpawns));
        if (MaxConcurrentExecutions < 1)
            throw new ArgumentException("MaxConcurrentExecutions 必须 >= 1", nameof(MaxConcurrentExecutions));
        if (MaxConcurrentForks < 0)
            throw new ArgumentException("MaxConcurrentForks 必须 >= 0（0=不限）", nameof(MaxConcurrentForks));
    }
}
