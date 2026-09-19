namespace Core.Agents.Coordinator;

/// <summary>
/// 执行统计计算器 — 从 AgentExecutionContext 集合计算统计信息与平均执行时间
/// </summary>
internal static class ExecutionStatisticsCalculator {
    /// <summary>
    /// 计算已完成上下文的平均执行时间（毫秒），无完成项时返回 null
    /// </summary>
    /// <param name="contexts">执行上下文集合</param>
    /// <returns>平均执行时间毫秒数，无完成项时为 null</returns>
    public static long? CalculateAverageExecutionTime(IEnumerable<AgentExecutionContext> contexts) {
        var completedContexts = contexts
            .Where(c => c.LastExecutionStart.HasValue && c.LastExecutionEnd.HasValue)
            .ToList();

        if (completedContexts.Count == 0) {
            return null;
        }

        var totalMs = completedContexts
            .Sum(c => (c.LastExecutionEnd.GetValueOrDefault() - c.LastExecutionStart.GetValueOrDefault()).TotalMilliseconds);

        return (long)(totalMs / completedContexts.Count);
    }

    /// <summary>
    /// 从执行上下文字典构建执行统计信息
    /// </summary>
    /// <param name="executionContexts">Agent ID 到执行上下文的映射</param>
    /// <returns>执行统计信息</returns>
    public static ExecutionStatistics BuildStatistics(ConcurrentDictionary<string, AgentExecutionContext> executionContexts) {
        var contexts = executionContexts.Values;
        var completedContexts = contexts.Where(c => c.Outcome != AgentOutcome.Pending).ToList();

        return new ExecutionStatistics {
            TotalAgents = executionContexts.Count,
            SuccessfulAgents = completedContexts.Count(c => c.Outcome == AgentOutcome.Succeeded),
            FailedAgents = completedContexts.Count(c => c.Outcome == AgentOutcome.Failed),
            CancelledAgents = contexts.Count(c => c.Outcome == AgentOutcome.Cancelled),
            TotalRetries = contexts.Sum(c => c.RetryCount),
            AverageExecutionTimeMs = CalculateAverageExecutionTime(contexts),
            ParallelExecutions = contexts.Count(c => c.ExecutionMode == ExecutionMode.Parallel),
            SequentialExecutions = contexts.Count(c => c.ExecutionMode == ExecutionMode.Sequential)
        };
    }
}