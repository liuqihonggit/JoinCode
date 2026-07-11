

namespace McpToolHandlers;

/// <summary>
/// 分析工具处理器 - 提供使用统计和分析功能
/// </summary>
[McpToolHandler(ToolCategory.Analytics, Optional = true)]
public class AnalyticsToolHandlers
{
    private readonly IAnalyticsService _analyticsService;

    public AnalyticsToolHandlers(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
    }

    /// <summary>
    /// 获取使用统计报告
    /// </summary>
    [McpTool(InteractionToolNameConstants.AnalyticsReport, "Get system usage statistics report", "analytics")]
    public Task<ToolResult> AnalyticsReportAsync(
        [McpToolParameter("Number of days for statistics (default 7)", Required = false)] int? days = null,
        CancellationToken cancellationToken = default)
    {
        var report = _analyticsService.GetUsageReport(days ?? 7);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.List.ToValue()} {L.T(StringKey.AnalyticsUsageReport)}");
        response.AppendLine(L.T(StringKey.AnalyticsStatPeriod, days ?? 7));
        response.AppendLine();
        response.AppendLine(L.T(StringKey.AnalyticsTotalEvents, report.TotalEvents));
        response.AppendLine(L.T(StringKey.AnalyticsToolCalls, report.TotalToolCalls));
        response.AppendLine(L.T(StringKey.AnalyticsToolSuccessRate, report.ToolSuccessRate.ToString("F1")));
        response.AppendLine(L.T(StringKey.AnalyticsAvgExecTime, report.AverageToolDurationMs.ToString("F0")));
        response.AppendLine(L.T(StringKey.AnalyticsErrorRate, report.ErrorRate.ToString("F1")));

        if (report.TopTools.Count > 0)
        {
            response.AppendLine();
            response.AppendLine($"{ObjectSymbol.Gear.ToValue()} {L.T(StringKey.AnalyticsTopTools)}");
            response.Append(string.Join(Environment.NewLine, report.TopTools.Take(5).Select(tool => $"  {tool.ToolName} {L.T(StringKey.AnalyticsToolEntry, tool.CallCount, tool.SuccessRate.ToString("F0"))}")));
            response.AppendLine();
        }

        if (report.DailyStats.Count > 0)
        {
            response.AppendLine();
            response.AppendLine($"{ObjectSymbol.DiamondOpen.ToValue()} {L.T(StringKey.AnalyticsDailyStats)}");
            response.Append(string.Join(Environment.NewLine, report.DailyStats.OrderByDescending(s => s.Key).Take(7).Select(s => L.T(StringKey.AnalyticsDailyEntry, s.Key.ToString("MM-dd"), s.Value.EventCount, s.Value.ToolCalls, s.Value.ErrorCount))));
            response.AppendLine();
        }

        return Task.FromResult(McpResultBuilder.Success().WithText(response.ToString()).Build());
    }

    /// <summary>
    /// 获取工具使用统计
    /// </summary>
    [McpTool(InteractionToolNameConstants.AnalyticsTools, "Get tool usage statistics details", "analytics")]
    public Task<ToolResult> AnalyticsToolsAsync(
        [McpToolParameter("Number of days for statistics (default 7)", Required = false)] int? days = null,
        CancellationToken cancellationToken = default)
    {
        var stats = _analyticsService.GetToolUsageStatistics(days ?? 7);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.Gear.ToValue()} {L.T(StringKey.AnalyticsToolUsageStats)}");
        response.AppendLine(L.T(StringKey.AnalyticsStatPeriod, days ?? 7));
        response.AppendLine(L.T(StringKey.AnalyticsToolCount, stats.Count));
        response.AppendLine();

        if (stats.Count == 0)
        {
            response.AppendLine(L.T(StringKey.AnalyticsNoToolData));
        }
        else
        {
            foreach (var tool in stats)
            {
                var statusIcon = tool.SuccessRate >= 90 ? StatusSymbol.Tick.ToValue() :
                                tool.SuccessRate >= 70 ? StatusSymbol.Warning.ToValue() : StatusSymbol.Cross.ToValue();

                response.AppendLine($"{statusIcon} {tool.ToolName}");
                response.AppendLine($"   {L.T(StringKey.AnalyticsCallSummary, tool.CallCount, tool.SuccessCount, tool.ErrorCount)}");
                response.AppendLine($"   {L.T(StringKey.AnalyticsSuccessRateDuration, tool.SuccessRate.ToString("F1"), tool.AverageDurationMs.ToString("F0"))}");

                if (tool.LastCallAt.HasValue)
                {
                    response.AppendLine($"   {L.T(StringKey.AnalyticsLastCall, tool.LastCallAt.Value.ToString("MM-dd HH:mm"))}");
                }

                response.AppendLine();
            }
        }

        return Task.FromResult(McpResultBuilder.Success().WithText(response.ToString()).Build());
    }

    /// <summary>
    /// 获取事件历史
    /// </summary>
    [McpTool(InteractionToolNameConstants.AnalyticsEvents, "Get analytics event history", "analytics")]
    public Task<ToolResult> AnalyticsEventsAsync(
        [McpToolParameter("Event type filter (optional)", Required = false)] string? event_type = null,
        [McpToolParameter("Result count limit", Required = false, DefaultValue = "50")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        AnalyticsEventType? type = null;
        if (!string.IsNullOrEmpty(event_type))
        {
            type = AnalyticsEventTypeExtensions.FromValue(event_type);
        }

        var events = _analyticsService.GetEventHistory(type, limit ?? 50);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.List.ToValue()} {L.T(StringKey.AnalyticsEventHistory)}");
        response.AppendLine(L.T(StringKey.AnalyticsEventCount, events.Count));
        response.AppendLine();

        if (events.Count == 0)
        {
            response.AppendLine(L.T(StringKey.AnalyticsNoEventData));
        }
        else
        {
            foreach (var evt in events)
            {
                var typeIcon = evt.Type switch
                {
                    AnalyticsEventType.ToolSuccess => StatusSymbol.Tick.ToValue(),
                    AnalyticsEventType.ToolError => StatusSymbol.Cross.ToValue(),
                    AnalyticsEventType.AgentStart => StatusSymbol.Play.ToValue(),
                    AnalyticsEventType.AgentComplete => StatusSymbol.Tick.ToValue(),
                    AnalyticsEventType.SystemError => StatusSymbol.Cross.ToValue(),
                    _ => StructureSymbol.Bullet.ToValue()
                };

                response.AppendLine($"{typeIcon} [{evt.Timestamp:MM-dd HH:mm:ss}] {evt.Type,-15} {evt.Name}");

                if (!string.IsNullOrEmpty(evt.AgentName))
                {
                    response.AppendLine($"   {L.T(StringKey.AnalyticsAgent, evt.AgentName)}");
                }

                if (evt.DurationMs.HasValue)
                {
                    response.AppendLine($"   {L.T(StringKey.AnalyticsDuration, evt.DurationMs.Value.ToString("F0"))}");
                }

                if (!string.IsNullOrEmpty(evt.ErrorMessage))
                {
                    response.AppendLine($"   {L.T(StringKey.AnalyticsErrorInfo, evt.ErrorMessage)}");
                }

                if (evt.Data.Count > 0)
                {
                    var dataStr = string.Join(", ", evt.Data.Take(3).Select(d => $"{d.Key}={d.Value}"));
                    response.AppendLine($"   {L.T(StringKey.AnalyticsDataInfo, dataStr)}");
                }

                response.AppendLine();
            }
        }

        return Task.FromResult(McpResultBuilder.Success().WithText(response.ToString()).Build());
    }

    /// <summary>
    /// 导出分析数据
    /// </summary>
    [McpTool(InteractionToolNameConstants.AnalyticsExport, "Export analytics data as JSON", "analytics")]
    public async Task<ToolResult> AnalyticsExportAsync(
        [McpToolParameter("Start date (optional)", Required = false)] string? start_date = null,
        [McpToolParameter("End date (optional)", Required = false)] string? end_date = null,
        CancellationToken cancellationToken = default)
    {
        DateTime? start = null;
        DateTime? end = null;

        if (!string.IsNullOrEmpty(start_date) && DateTime.TryParse(start_date, out var parsedStart))
        {
            start = parsedStart;
        }

        if (!string.IsNullOrEmpty(end_date) && DateTime.TryParse(end_date, out var parsedEnd))
        {
            end = parsedEnd;
        }

        var json = await _analyticsService.ExportDataAsync(start, end, cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.ArrowUp.ToValue()} {L.T(StringKey.AnalyticsDataExport)}");
        response.AppendLine();

        if (start.HasValue)
        {
            response.AppendLine(L.T(StringKey.AnalyticsStartDate, start.Value.ToString("yyyy-MM-dd")));
        }

        if (end.HasValue)
        {
            response.AppendLine(L.T(StringKey.AnalyticsEndDate, end.Value.ToString("yyyy-MM-dd")));
        }

        response.AppendLine();
        response.AppendLine(L.T(StringKey.AnalyticsJsonData));
        response.AppendLine("```json");
        response.AppendLine(json[..Math.Min(WorkflowConstants.Limits.JsonTruncateLength, json.Length)]);

        if (json.Length > WorkflowConstants.Limits.JsonTruncateLength)
        {
            response.AppendLine("...");
            response.AppendLine(L.T(StringKey.AnalyticsTruncated, json.Length));
        }

        response.AppendLine("```");

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 清除分析数据
    /// </summary>
    [McpTool(InteractionToolNameConstants.AnalyticsClear, "Clear analytics history data", "analytics")]
    public Task<ToolResult> AnalyticsClearAsync(
        [McpToolParameter("Clear data older than N days (optional)", Required = false)] int? older_than_days = null,
        [McpToolParameter("Confirm clear (enter 'yes' to confirm)")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (confirm != "yes")
        {
            return Task.FromResult(McpResultBuilder.Error()
                .WithText(L.T(StringKey.AnalyticsConfirmClear))
                .Build());
        }

        _analyticsService.ClearHistory(older_than_days);

        var message = older_than_days.HasValue
            ? $"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.AnalyticsClearedOlder, older_than_days.Value)}"
            : $"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.AnalyticsClearedAll)}";

        return Task.FromResult(McpResultBuilder.Success().WithText(message).Build());
    }
}
