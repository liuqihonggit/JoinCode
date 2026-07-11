


namespace McpToolHandlers;

/// <summary>
/// 代理摘要工具处理器 - 提供代理执行统计和分析功能
/// </summary>
[McpToolHandler(ToolCategory.Analytics, Optional = true)]
public class AgentSummaryToolHandlers
{
    private readonly IAgentSummaryService _agentSummaryService;

    public AgentSummaryToolHandlers(IAgentSummaryService agentSummaryService)
    {
        _agentSummaryService = agentSummaryService ?? throw new ArgumentNullException(nameof(agentSummaryService));
    }

    /// <summary>
    /// 获取系统整体统计
    /// </summary>
    [McpTool(AgentToolNameConstants.AgentSystemStats, "Get overall agent execution statistics", "analytics")]
    public Task<ToolResult> AgentSystemStatsAsync(
        CancellationToken cancellationToken = default)
    {
        var stats = _agentSummaryService.GetSystemStatistics();

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.SystemAgentStats, ObjectSymbol.List.ToValue()));
        response.AppendLine();
        response.AppendLine(L.T(StringKey.LabelTotalAgents, stats.TotalAgents));
        response.AppendLine(L.T(StringKey.LabelActiveAgents, stats.ActiveAgents));
        response.AppendLine(L.T(StringKey.LabelTotalExecutions, stats.TotalExecutions));
        response.AppendLine(L.T(StringKey.LabelRunningExecutions, stats.RunningExecutions));
        response.AppendLine(L.T(StringKey.LabelTodayExecutions, stats.TodayExecutions));
        response.AppendLine(L.T(StringKey.LabelWeekExecutions, stats.WeekExecutions));
        response.AppendLine();
        response.AppendLine(L.T(StringKey.LabelStatisticsTime, stats.StatisticsAt.ToString("yyyy-MM-dd HH:mm:ss")));

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }

    /// <summary>
    /// 获取所有代理统计
    /// </summary>
    [McpTool(AgentToolNameConstants.AgentListStats, "List statistics for all agents", "analytics")]
    public Task<ToolResult> AgentListStatsAsync(
        CancellationToken cancellationToken = default)
    {
        var allStats = _agentSummaryService.GetAllAgentStatistics();

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.AgentStatsList, ObjectSymbol.ArrowUp.ToValue()));
        response.AppendLine(L.T(StringKey.TotalAgentsCount, allStats.Count));
        response.AppendLine();

        if (allStats.Count == 0)
        {
            response.AppendLine(L.T(StringKey.NoAgentRecords));
        }
        else
        {
            foreach (var stats in allStats.OrderByDescending(s => s.TotalExecutions))
            {
        response.AppendLine($"{ObjectSymbol.Agent.ToValue()} {stats.AgentName}");
                response.AppendLine(L.T(StringKey.LabelExecCount, stats.TotalExecutions, stats.SuccessfulExecutions, stats.FailedExecutions));
                response.AppendLine(L.T(StringKey.LabelSuccessRate, stats.SuccessRate));

                if (stats.AverageExecutionTime.HasValue)
                {
                    response.AppendLine(L.T(StringKey.LabelAvgExecTime, DurationFormatter.Format(stats.AverageExecutionTime.Value, DurationFormatOptions.MostSignificant)));
                }

                response.AppendLine(L.T(StringKey.LabelTotalToolCalls, stats.TotalToolCalls));

                if (stats.LastExecutionAt.HasValue)
                {
                    response.AppendLine(L.T(StringKey.LabelLastExecution, stats.LastExecutionAt.Value.ToString("yyyy-MM-dd HH:mm")));
                }

                response.AppendLine();
            }
        }

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }

    /// <summary>
    /// 获取指定代理的统计
    /// </summary>
    [McpTool(AgentToolNameConstants.AgentStats, "Get detailed statistics for a specific agent", "analytics")]
    public Task<ToolResult> AgentStatsAsync(
        [McpToolParameter("Agent name")] string agent_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_name))
        {
            return Task.FromResult(ToolResultBuilder.Error().WithText(L.T(StringKey.AgentNameCannotBeEmpty)).Build());
        }

        var stats = _agentSummaryService.GetAgentStatistics(agent_name);

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.AgentStatsFor, ObjectSymbol.List.ToValue(), agent_name));
        response.AppendLine();
        response.AppendLine(L.T(StringKey.LabelTotalExecutions, stats.TotalExecutions));
        response.AppendLine($"{L.T(StringKey.LabelSuccess, stats.SuccessfulExecutions)} | {L.T(StringKey.LabelFailed, stats.FailedExecutions)}");
        response.AppendLine(L.T(StringKey.LabelSuccessRate, stats.SuccessRate));
        response.AppendLine(L.T(StringKey.LabelTotalExecTime, DurationFormatter.Format(stats.TotalExecutionTime, DurationFormatOptions.MostSignificant)));

        if (stats.AverageExecutionTime.HasValue)
        {
            response.AppendLine(L.T(StringKey.LabelAvgExecTimeFor, DurationFormatter.Format(stats.AverageExecutionTime.Value, DurationFormatOptions.MostSignificant)));
        }

        response.AppendLine(L.T(StringKey.LabelTotalToolCalls, stats.TotalToolCalls));

        if (stats.LastExecutionAt.HasValue)
        {
            response.AppendLine(L.T(StringKey.LabelLastExecution, stats.LastExecutionAt.Value.ToString("yyyy-MM-dd HH:mm:ss")));
        }

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }

    /// <summary>
    /// 获取代理执行历史
    /// </summary>
    [McpTool(AgentToolNameConstants.AgentHistory, "Get execution history for an agent", "analytics")]
    public Task<ToolResult> AgentHistoryAsync(
        [McpToolParameter("Agent name")] string agent_name,
        [McpToolParameter("Result count limit", Required = false, DefaultValue = "10")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_name))
        {
            return Task.FromResult(ToolResultBuilder.Error().WithText(L.T(StringKey.AgentNameCannotBeEmpty)).Build());
        }

        var history = _agentSummaryService.GetAgentExecutionHistory(agent_name, limit ?? 10);

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.AgentHistoryFor, ObjectSymbol.List.ToValue(), agent_name));
        response.AppendLine(L.T(StringKey.RecentRecordsCount, history.Count));
        response.AppendLine();

        if (history.Count == 0)
        {
            response.AppendLine(L.T(StringKey.NoExecutionRecords));
        }
        else
        {
            foreach (var execution in history)
            {
                var statusIcon = execution.Status switch
                {
                    TaskExecutionStatus.Completed => StatusSymbol.Tick.ToValue(),
                    TaskExecutionStatus.Failed => StatusSymbol.Cross.ToValue(),
                    TaskExecutionStatus.Running => StatusSymbol.Refresh.ToValue(),
                    TaskExecutionStatus.Cancelled => StatusSymbol.Prohibited.ToValue(),
                    _ => StatusSymbol.Circle.ToValue()
                };

                response.AppendLine($"{statusIcon} [{execution.ExecutionId}] {execution.CreatedAt:MM-dd HH:mm}");

                if (!string.IsNullOrEmpty(execution.TaskDescription))
                {
                    response.AppendLine(L.T(StringKey.LabelTask, execution.TaskDescription[..Math.Min(40, execution.TaskDescription.Length)] + "..."));
                }

                response.AppendLine(L.T(StringKey.SyncLabelStatus, execution.Status));

                if (execution.Metrics.Duration.HasValue)
                {
                    response.AppendLine(L.T(StringKey.LabelDuration, DurationFormatter.Format(execution.Metrics.Duration.Value, DurationFormatOptions.MostSignificant)));
                }

                if (execution.Metrics.StepsExecuted > 0)
                {
                    response.AppendLine(L.T(StringKey.LabelSteps, execution.Metrics.StepsExecuted, execution.Metrics.StepsSucceeded));
                }

                if (execution.Metrics.ToolCallsCount > 0)
                {
                    response.AppendLine(L.T(StringKey.LabelToolCalls, execution.Metrics.ToolCallsCount));
                }

                response.AppendLine();
            }
        }

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }

    /// <summary>
    /// 获取正在运行的执行
    /// </summary>
    [McpTool(AgentToolNameConstants.AgentRunningStats, "Get currently running agent executions", "analytics")]
    public Task<ToolResult> AgentRunningAsync(
        CancellationToken cancellationToken = default)
    {
        var running = _agentSummaryService.GetRunningExecutions();

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.RunningExecutions, StatusSymbol.Refresh.ToValue()));
        response.AppendLine(L.T(StringKey.RunningCount, running.Count));
        response.AppendLine();

        if (running.Count == 0)
        {
            response.AppendLine(L.T(StringKey.NoRunningExecutions));
        }
        else
        {
            foreach (var execution in running.OrderByDescending(e => e.CreatedAt))
            {
                response.AppendLine($"{StatusSymbol.Refresh.ToValue()} [{execution.ExecutionId}] {execution.AgentName}");

                if (!string.IsNullOrEmpty(execution.TaskDescription))
                {
                    response.AppendLine(L.T(StringKey.LabelTask, execution.TaskDescription[..Math.Min(40, execution.TaskDescription.Length)] + "..."));
                }

                response.AppendLine(L.T(StringKey.LabelStartTime, execution.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")));

                if (execution.Metrics.Duration.HasValue)
                {
                    response.AppendLine(L.T(StringKey.LabelAlreadyRunning, DurationFormatter.Format(execution.Metrics.Duration.Value, DurationFormatOptions.MostSignificant)));
                }

                if (execution.Metrics.StepsExecuted > 0)
                {
                    response.AppendLine(L.T(StringKey.LabelSteps, execution.Metrics.StepsExecuted, execution.Metrics.StepsSucceeded));
                }

                if (execution.Metrics.ToolCallsCount > 0)
                {
                    response.AppendLine(L.T(StringKey.LabelToolCalls, execution.Metrics.ToolCallsCount));
                }

                response.AppendLine();
            }
        }

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }

    /// <summary>
    /// 获取执行详情
    /// </summary>
    [McpTool(AgentToolNameConstants.AgentExecutionDetail, "Get detailed information about a specific execution", "analytics")]
    public Task<ToolResult> AgentExecutionDetailAsync(
        [McpToolParameter("Execution ID")] string execution_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(execution_id))
        {
            return Task.FromResult(ToolResultBuilder.Error().WithText(L.T(StringKey.ExecutionIdCannotBeEmpty)).Build());
        }

        var execution = _agentSummaryService.GetExecutionSummary(execution_id);

        if (execution == null)
        {
            return Task.FromResult(ToolResultBuilder.Error().WithText(L.T(StringKey.ExecutionNotFound, execution_id)).Build());
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.ExecutionDetail, ObjectSymbol.List.ToValue(), execution.ExecutionId));
        response.AppendLine();
        response.AppendLine(L.T(StringKey.LabelAgent, execution.AgentName));
        response.AppendLine(L.T(StringKey.SyncLabelStatus, execution.Status));
        response.AppendLine(L.T(StringKey.LabelCreatedTime, execution.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")));

        if (!string.IsNullOrEmpty(execution.TaskDescription))
        {
            response.AppendLine(L.T(StringKey.LabelTaskDescription, execution.TaskDescription));
        }

        response.AppendLine();
        response.AppendLine(L.T(StringKey.ExecutionMetrics, ObjectSymbol.List.ToValue()));

        if (execution.Metrics.StartedAt.HasValue)
        {
            response.AppendLine(L.T(StringKey.LabelStartTime, execution.Metrics.StartedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")));
        }

        if (execution.Metrics.CompletedAt.HasValue)
        {
            response.AppendLine(L.T(StringKey.LabelEndTime, execution.Metrics.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")));
        }

        if (execution.Metrics.Duration.HasValue)
        {
            response.AppendLine(L.T(StringKey.LabelDurationTime, DurationFormatter.Format(execution.Metrics.Duration.Value, DurationFormatOptions.MostSignificant)));
        }

        response.AppendLine(L.T(StringKey.LabelExecSteps, execution.Metrics.StepsExecuted));
        response.AppendLine(L.T(StringKey.LabelSuccessSteps, execution.Metrics.StepsSucceeded));
        response.AppendLine(L.T(StringKey.LabelFailedSteps, execution.Metrics.StepsFailed));
        response.AppendLine(L.T(StringKey.LabelToolCalls, execution.Metrics.ToolCallsCount));
        response.AppendLine(L.T(StringKey.LabelMessagesSent, execution.Metrics.MessagesSent));
        response.AppendLine(L.T(StringKey.LabelMessagesReceived, execution.Metrics.MessagesReceived));

        if (!string.IsNullOrEmpty(execution.ResultSummary))
        {
            response.AppendLine();
            response.AppendLine(L.T(StringKey.ResultSummary, ObjectSymbol.Pencil.ToValue()));
            response.AppendLine(execution.ResultSummary);
        }

        if (!string.IsNullOrEmpty(execution.ErrorMessage))
        {
            response.AppendLine();
            response.AppendLine(L.T(StringKey.ErrorMessage, StatusSymbol.Cross.ToValue()));
            response.AppendLine(execution.ErrorMessage);
        }

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }

    /// <summary>
    /// 清除历史记录
    /// </summary>
    [McpTool(AgentToolNameConstants.AgentClearHistory, "Clear agent execution history", "analytics")]
    public Task<ToolResult> AgentClearHistoryAsync(
        [McpToolParameter("Clear records older than N days (optional)", Required = false)] int? older_than_days = null,
        [McpToolParameter("Confirm clear (enter 'yes' to confirm)")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (confirm != "yes")
        {
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(L.T(StringKey.ConfirmClearHistory))
                .Build());
        }

        _agentSummaryService.ClearHistory(older_than_days);

        var message = older_than_days.HasValue
            ? L.T(StringKey.HistoryClearedDays, StatusSymbol.Tick.ToValue(), older_than_days.Value)
            : L.T(StringKey.HistoryClearedAll, StatusSymbol.Tick.ToValue());

        return Task.FromResult(ToolResultBuilder.Success().WithText(message).Build());
    }

}
