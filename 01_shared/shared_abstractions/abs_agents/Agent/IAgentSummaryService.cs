namespace JoinCode.Abstractions.Interfaces;

#region Agent Summary Models

/// <summary>
/// 代理执行指标
/// </summary>
public sealed record AgentExecutionMetrics
{
    /// <summary>
    /// 执行开始时间
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// 执行结束时间
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// 执行持续时间
    /// </summary>
    public TimeSpan? Duration => StartedAt.HasValue && CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : StartedAt.HasValue ? DateTime.UtcNow - StartedAt.Value : null;

    /// <summary>
    /// 执行的步骤数
    /// </summary>
    public int StepsExecuted { get; init; }

    /// <summary>
    /// 成功的步骤数
    /// </summary>
    public int StepsSucceeded { get; init; }

    /// <summary>
    /// 失败的步骤数
    /// </summary>
    public int StepsFailed { get; init; }

    /// <summary>
    /// 工具调用次数
    /// </summary>
    public int ToolCallsCount { get; init; }

    /// <summary>
    /// 发送的消息数
    /// </summary>
    public int MessagesSent { get; init; }

    /// <summary>
    /// 接收的消息数
    /// </summary>
    public int MessagesReceived { get; init; }

    /// <summary>
    /// 处理的Token数（估算）
    /// </summary>
    public int TokensProcessed { get; init; }
}

/// <summary>
/// 代理执行摘要
/// </summary>
public sealed record AgentExecutionSummary
{
    /// <summary>
    /// 执行ID
    /// </summary>
    public required string ExecutionId { get; init; }

    /// <summary>
    /// 代理名称
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// 任务描述
    /// </summary>
    public string? TaskDescription { get; init; }

    /// <summary>
    /// 执行状态
    /// </summary>
    public TaskExecutionStatus Status { get; init; }

    /// <summary>
    /// 执行指标
    /// </summary>
    public AgentExecutionMetrics Metrics { get; init; } = new();

    /// <summary>
    /// 执行结果摘要
    /// </summary>
    public string? ResultSummary { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 代理统计信息
/// </summary>
public sealed record AgentStatistics
{
    /// <summary>
    /// 代理名称
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// 总执行次数
    /// </summary>
    public int TotalExecutions { get; init; }

    /// <summary>
    /// 成功次数
    /// </summary>
    public int SuccessfulExecutions { get; init; }

    /// <summary>
    /// 失败次数
    /// </summary>
    public int FailedExecutions { get; init; }

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => TotalExecutions > 0
        ? (double)SuccessfulExecutions / TotalExecutions * 100
        : 0;

    /// <summary>
    /// 平均执行时间
    /// </summary>
    public TimeSpan? AverageExecutionTime { get; init; }

    /// <summary>
    /// 总执行时间
    /// </summary>
    public TimeSpan TotalExecutionTime { get; init; }

    /// <summary>
    /// 总工具调用次数
    /// </summary>
    public int TotalToolCalls { get; init; }

    /// <summary>
    /// 最后执行时间
    /// </summary>
    public DateTime? LastExecutionAt { get; init; }
}

/// <summary>
/// 系统整体统计
/// </summary>
public sealed record SystemStatistics
{
    /// <summary>
    /// 总代理数
    /// </summary>
    public int TotalAgents { get; init; }

    /// <summary>
    /// 活跃代理数
    /// </summary>
    public int ActiveAgents { get; init; }

    /// <summary>
    /// 总执行次数
    /// </summary>
    public int TotalExecutions { get; init; }

    /// <summary>
    /// 正在运行的执行数
    /// </summary>
    public int RunningExecutions { get; init; }

    /// <summary>
    /// 今日执行次数
    /// </summary>
    public int TodayExecutions { get; init; }

    /// <summary>
    /// 本周执行次数
    /// </summary>
    public int WeekExecutions { get; init; }

    /// <summary>
    /// 统计时间
    /// </summary>
    public DateTime StatisticsAt { get; init; } = DateTime.UtcNow;
}

#endregion

/// <summary>
/// 代理摘要服务接口
/// </summary>
public interface IAgentSummaryService
{
    /// <summary>
    /// 开始执行跟踪
    /// </summary>
    AgentExecutionSummary StartExecution(string agentName, string? taskDescription = null);

    /// <summary>
    /// 更新执行状态
    /// </summary>
    void UpdateExecution(string executionId, TaskExecutionStatus status, string? resultSummary = null);

    /// <summary>
    /// 完成执行
    /// </summary>
    void CompleteExecution(string executionId, bool success, string? resultSummary = null, string? errorMessage = null);

    /// <summary>
    /// 记录工具调用
    /// </summary>
    void RecordToolCall(string executionId, string toolName);

    /// <summary>
    /// 记录消息
    /// </summary>
    void RecordMessage(string executionId, bool sent);

    /// <summary>
    /// 记录步骤
    /// </summary>
    void RecordStep(string executionId, bool succeeded);

    /// <summary>
    /// 获取执行摘要
    /// </summary>
    AgentExecutionSummary? GetExecutionSummary(string executionId);

    /// <summary>
    /// 获取代理的执行历史
    /// </summary>
    List<AgentExecutionSummary> GetAgentExecutionHistory(string agentName, int limit = 10);

    /// <summary>
    /// 获取代理统计
    /// </summary>
    AgentStatistics GetAgentStatistics(string agentName);

    /// <summary>
    /// 获取所有代理统计
    /// </summary>
    List<AgentStatistics> GetAllAgentStatistics();

    /// <summary>
    /// 获取系统统计
    /// </summary>
    SystemStatistics GetSystemStatistics();

    /// <summary>
    /// 获取正在运行的执行
    /// </summary>
    List<AgentExecutionSummary> GetRunningExecutions();

    /// <summary>
    /// 清除历史记录
    /// </summary>
    void ClearHistory(int? olderThanDays = null);
}
