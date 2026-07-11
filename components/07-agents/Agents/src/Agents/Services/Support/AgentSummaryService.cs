namespace Core.Agents;

/// <summary>
/// 代理摘要服务实现
/// </summary>
[Register]
public sealed partial class AgentSummaryService : IAgentSummaryService
{
    private readonly ConcurrentDictionary<string, AgentExecutionSummary> _executions = new();
    private readonly ConcurrentDictionary<string, AgentMetricsAccumulator> _metrics = new();
    [Inject] private readonly ILogger<AgentSummaryService>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly IClockService _clock;

    /// <inheritdoc />
    public AgentExecutionSummary StartExecution(string agentName, string? taskDescription = null)
    {
        var executionId = Guid.NewGuid().ToString("N")[..8];

        var summary = new AgentExecutionSummary
        {
            ExecutionId = executionId,
            AgentName = agentName,
            TaskDescription = taskDescription,
            Status = TaskExecutionStatus.Running,
            Metrics = new AgentExecutionMetrics
            {
                StartedAt = _clock.GetUtcNow()
            }
        };

        _executions[executionId] = summary;

        _metrics.AddOrUpdate(agentName,
            _ => new AgentMetricsAccumulator { FirstExecutionAt = _clock.GetUtcNow() },
            (_, existing) => existing);

        _logger?.LogInformation("开始跟踪代理执行 {ExecutionId}: {AgentName}", executionId, agentName);

        RecordAgentExecutionMetrics("started", agentName);

        return summary;
    }

    /// <inheritdoc />
    public void UpdateExecution(string executionId, TaskExecutionStatus status, string? resultSummary = null)
    {
        if (_executions.TryGetValue(executionId, out var summary))
        {
            _executions[executionId] = summary with
            {
                Status = status,
                ResultSummary = resultSummary ?? summary.ResultSummary,
                LastUpdatedAt = _clock.GetUtcNow()
            };

            _logger?.LogDebug("更新执行状态 {ExecutionId}: {Status}", executionId, status);
        }
    }

    /// <inheritdoc />
    public void CompleteExecution(string executionId, bool success, string? resultSummary = null, string? errorMessage = null)
    {
        if (_executions.TryGetValue(executionId, out var summary))
        {
            var completedAt = _clock.GetUtcNow();
            var status = success ? TaskExecutionStatus.Completed :
                        errorMessage != null ? TaskExecutionStatus.Failed :
                        TaskExecutionStatus.Cancelled;

            _executions[executionId] = summary with
            {
                Status = status,
                ResultSummary = resultSummary ?? summary.ResultSummary,
                ErrorMessage = errorMessage,
                LastUpdatedAt = completedAt,
                Metrics = summary.Metrics with
                {
                    CompletedAt = completedAt
                }
            };

            if (_metrics.TryGetValue(summary.AgentName, out var accumulator))
            {
                accumulator.TotalExecutions++;
                if (success) accumulator.SuccessfulExecutions++;
                else accumulator.FailedExecutions++;
                accumulator.LastExecutionAt = completedAt;

                if (summary.Metrics.Duration.HasValue)
                {
                    accumulator.TotalExecutionTime += summary.Metrics.Duration.Value;
                }

                accumulator.TotalToolCalls += summary.Metrics.ToolCallsCount;
            }

            _logger?.LogInformation(
                "执行完成 {ExecutionId}: {Status}, 持续时间: {Duration}",
                executionId,
                status,
                summary.Metrics.Duration);

            RecordAgentCompletionMetrics(summary.AgentName, status, summary.Metrics.Duration);
        }
    }

    /// <inheritdoc />
    public void RecordToolCall(string executionId, string toolName)
    {
        if (_executions.TryGetValue(executionId, out var summary))
        {
            _executions[executionId] = summary with
            {
                Metrics = summary.Metrics with
                {
                    ToolCallsCount = summary.Metrics.ToolCallsCount + 1
                },
                LastUpdatedAt = _clock.GetUtcNow()
            };
        }
    }

    /// <inheritdoc />
    public void RecordMessage(string executionId, bool sent)
    {
        if (_executions.TryGetValue(executionId, out var summary))
        {
            _executions[executionId] = summary with
            {
                Metrics = summary.Metrics with
                {
                    MessagesSent = sent ? summary.Metrics.MessagesSent + 1 : summary.Metrics.MessagesSent,
                    MessagesReceived = !sent ? summary.Metrics.MessagesReceived + 1 : summary.Metrics.MessagesReceived
                },
                LastUpdatedAt = _clock.GetUtcNow()
            };
        }
    }

    /// <inheritdoc />
    public void RecordStep(string executionId, bool succeeded)
    {
        if (_executions.TryGetValue(executionId, out var summary))
        {
            _executions[executionId] = summary with
            {
                Metrics = summary.Metrics with
                {
                    StepsExecuted = summary.Metrics.StepsExecuted + 1,
                    StepsSucceeded = succeeded ? summary.Metrics.StepsSucceeded + 1 : summary.Metrics.StepsSucceeded,
                    StepsFailed = !succeeded ? summary.Metrics.StepsFailed + 1 : summary.Metrics.StepsFailed
                },
                LastUpdatedAt = _clock.GetUtcNow()
            };
        }
    }

    /// <inheritdoc />
    public AgentExecutionSummary? GetExecutionSummary(string executionId)
    {
        return _executions.TryGetValue(executionId, out var summary) ? summary : null;
    }

    /// <inheritdoc />
    public List<AgentExecutionSummary> GetAgentExecutionHistory(string agentName, int limit = 10)
    {
        return _executions.Values
            .Where(e => e.AgentName == agentName)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToList();
    }

    /// <inheritdoc />
    public AgentStatistics GetAgentStatistics(string agentName)
    {
        var executions = _executions.Values.Where(e => e.AgentName == agentName).ToList();
        var accumulator = _metrics.GetValueOrDefault(agentName);

        var successfulExecutions = executions.Count(e => e.Status == TaskExecutionStatus.Completed);
        var failedExecutions = executions.Count(e => e.Status == TaskExecutionStatus.Failed);

        var totalDuration = executions
            .Where(e => e.Metrics.Duration.HasValue)
            .Sum(e => e.Metrics.Duration!.Value.TotalMilliseconds);

        var avgDuration = executions.Count > 0
            ? TimeSpan.FromMilliseconds(totalDuration / executions.Count)
            : (TimeSpan?)null;

        return new AgentStatistics
        {
            AgentName = agentName,
            TotalExecutions = executions.Count,
            SuccessfulExecutions = successfulExecutions,
            FailedExecutions = failedExecutions,
            AverageExecutionTime = avgDuration,
            TotalExecutionTime = accumulator?.TotalExecutionTime ?? TimeSpan.Zero,
            TotalToolCalls = accumulator?.TotalToolCalls ?? 0,
            LastExecutionAt = accumulator?.LastExecutionAt
        };
    }

    /// <inheritdoc />
    public List<AgentStatistics> GetAllAgentStatistics()
    {
        var agentNames = _executions.Values.Select(e => e.AgentName).Distinct();
        return agentNames.Select(GetAgentStatistics).ToList();
    }

    /// <inheritdoc />
    public SystemStatistics GetSystemStatistics()
    {
        var now = _clock.GetUtcNow();
        var today = now.Date;
        var weekStart = today.AddDays(-(int)today.DayOfWeek);

        var executions = _executions.Values.ToList();
        var runningExecutions = executions.Where(e => e.Status == TaskExecutionStatus.Running).ToList();

        return new SystemStatistics
        {
            TotalAgents = executions.Select(e => e.AgentName).Distinct().Count(),
            ActiveAgents = runningExecutions.Select(e => e.AgentName).Distinct().Count(),
            TotalExecutions = executions.Count,
            RunningExecutions = runningExecutions.Count,
            TodayExecutions = executions.Count(e => e.CreatedAt.Date == today),
            WeekExecutions = executions.Count(e => e.CreatedAt >= weekStart)
        };
    }

    /// <inheritdoc />
    public List<AgentExecutionSummary> GetRunningExecutions()
    {
        return _executions.Values
            .Where(e => e.Status == TaskExecutionStatus.Running)
            .OrderByDescending(e => e.CreatedAt)
            .ToList();
    }

    /// <inheritdoc />
    public void ClearHistory(int? olderThanDays = null)
    {
        if (olderThanDays.HasValue)
        {
            var cutoffDate = _clock.GetUtcNow().AddDays(-olderThanDays.Value);
            var keysToRemove = _executions
                .Where(e => e.Value.CreatedAt < cutoffDate && e.Value.Status != TaskExecutionStatus.Running)
                .Select(e => e.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _executions.TryRemove(key, out _);
            }

            _logger?.LogInformation("已清除 {Count} 条历史记录（早于 {Days} 天）", keysToRemove.Count, olderThanDays.Value);
        }
        else
        {
            var keysToRemove = _executions
                .Where(e => e.Value.Status != TaskExecutionStatus.Running)
                .Select(e => e.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _executions.TryRemove(key, out _);
            }

            _logger?.LogInformation("已清除 {Count} 条历史记录", keysToRemove.Count);
        }
    }

    private void RecordAgentExecutionMetrics(string operation, string agentName)
        => _telemetryService?.RecordCount("agent.summary.execution.count", new Dictionary<string, string> { ["operation"] = operation, ["agent"] = agentName }, "count", "Agent execution count");

    private void RecordAgentCompletionMetrics(string agentName, TaskExecutionStatus status, TimeSpan? duration)
    {
        _telemetryService?.RecordCount("agent.summary.completion.count", new Dictionary<string, string> { ["agent"] = agentName, ["status"] = status.ToString() }, "count", "Agent completion count");
        if (duration.HasValue)
            _telemetryService?.RecordHistogram("agent.summary.execution.duration", duration.Value.TotalMilliseconds, new Dictionary<string, string> { ["agent"] = agentName }, "ms", "Agent execution duration");
    }

    #region Private Classes

    private class AgentMetricsAccumulator
    {
        public int TotalExecutions { get; set; }
        public int SuccessfulExecutions { get; set; }
        public int FailedExecutions { get; set; }
        public TimeSpan TotalExecutionTime { get; set; }
        public int TotalToolCalls { get; set; }
        public DateTime? FirstExecutionAt { get; set; }
        public DateTime? LastExecutionAt { get; set; }
    }

    #endregion
}
