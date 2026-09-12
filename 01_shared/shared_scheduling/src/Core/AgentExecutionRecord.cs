
namespace Core.Scheduling;

/// <summary>
/// Agent 执行记录 - 用于跟踪任务的 Agent 执行历史
/// </summary>
public sealed class AgentExecutionRecord
{
    /// <summary>
    /// 任务 ID
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// 任务名称
    /// </summary>
    public required string TaskName { get; init; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public required DateTime StartTime { get; init; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public required DateTime EndTime { get; init; }

    /// <summary>
    /// 总执行时长
    /// </summary>
    public required TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Agent 执行结果列表
    /// </summary>
    public required List<SubAgentResult> AgentResults { get; init; }

    /// <summary>
    /// 成功执行的 Agent 数量
    /// </summary>
    public int SuccessCount => AgentResults?.Count(r => r.IsSuccess) ?? 0;

    /// <summary>
    /// 失败的 Agent 数量
    /// </summary>
    public int FailureCount => AgentResults?.Count(r => !r.IsSuccess) ?? 0;

    /// <summary>
    /// 是否全部成功（空列表返回false）
    /// </summary>
    public bool AllSuccess => AgentResults?.Count > 0 && AgentResults.All(r => r.IsSuccess);

    /// <summary>
    /// 获取合并后的输出
    /// </summary>
    public string GetMergedOutput()
    {
        if (AgentResults == null || AgentResults.Count == 0)
        {
            return string.Empty;
        }

        var successfulOutputs = AgentResults
            .Where(r => r.IsSuccess && !string.IsNullOrEmpty(r.Output))
            .Select(r => r.Output ?? string.Empty)
            .ToList();

        return string.Join("\n\n", successfulOutputs);
    }
}
