namespace JoinCode.Cli;

/// <summary>
/// 洞察数据聚合器 — CLI 简化版
/// </summary>
public static class InsightDataAggregator
{
    /// <summary>
    /// 聚合会话数据
    /// </summary>
    public static AggregatedInsightData Aggregate(IReadOnlyList<InsightSessionMeta> sessions)
    {
        if (sessions.Count == 0)
        {
            return new AggregatedInsightData
            {
                TotalSessions = 0,
                TotalMessages = 0,
                TotalInputTokens = 0,
                TotalOutputTokens = 0,
                DailyActivities = []
            };
        }

        var totalMessages = 0;
        var totalInputTokens = 0L;
        var totalOutputTokens = 0L;
        var dailyMap = new Dictionary<DateOnly, DailyActivity>();
        var toolCounts = new Dictionary<string, int>();
        var languageCounts = new Dictionary<string, int>();
        var projectCounts = new Dictionary<string, int>();

        foreach (var session in sessions)
        {
            totalMessages += session.UserMessageCount + session.AssistantMessageCount;
            totalInputTokens += session.InputTokens;
            totalOutputTokens += session.OutputTokens;

            var dateKey = DateOnly.FromDateTime(session.StartTime);
            if (!dailyMap.TryGetValue(dateKey, out var daily))
            {
                daily = new DailyActivity { Date = dateKey };
                dailyMap[dateKey] = daily;
            }

            if (!string.IsNullOrEmpty(session.ProjectPath) && !projectCounts.TryAdd(session.ProjectPath, 1))
            {
                projectCounts[session.ProjectPath]++;
            }

            foreach (var (tool, count) in session.ToolCounts)
            {
                if (!toolCounts.TryAdd(tool, count))
                {
                    toolCounts[tool] += count;
                }
            }

            foreach (var (lang, count) in session.Languages)
            {
                if (!languageCounts.TryAdd(lang, count))
                {
                    languageCounts[lang] += count;
                }
            }
        }

        return new AggregatedInsightData
        {
            TotalSessions = sessions.Count,
            TotalMessages = totalMessages,
            TotalInputTokens = totalInputTokens,
            TotalOutputTokens = totalOutputTokens,
            DailyActivities = dailyMap.Values.OrderBy(d => d.Date).ToList(),
            ToolCounts = toolCounts,
            Languages = languageCounts,
            Projects = projectCounts,
            DaysActive = dailyMap.Count,
            TotalCostUsd = sessions.Sum(s => s.EstimatedCostUsd)
        };
    }

    /// <summary>
    /// 格式化统计报告
    /// </summary>
    public static string FormatStatsReport(AggregatedInsightData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleConstants.Bold}会话统计{AnsiStyleConstants.Reset}");
        sb.AppendLine($"  总会话数: {data.TotalSessions}");
        sb.AppendLine($"  总消息数: {data.TotalMessages:N0}");
        sb.AppendLine($"  输入 Token: {data.TotalInputTokens:N0}");
        sb.AppendLine($"  输出 Token: {data.TotalOutputTokens:N0}");
        sb.AppendLine($"  活跃天数: {data.DaysActive}");

        if (data.TotalCostUsd > 0)
        {
            sb.AppendLine($"  总成本: ${data.TotalCostUsd:F2}");
        }

        if (!string.IsNullOrEmpty(data.FavoriteModel))
        {
            sb.AppendLine($"  常用模型: {data.FavoriteModel}");
        }

        return sb.ToString();
    }
}

/// <summary>
/// Facet 聚合器 — CLI 简化版
/// </summary>
public static class FacetAggregator
{
    public static FacetSummary? Aggregate(IReadOnlyList<SessionFacets> facets)
    {
        if (facets.Count == 0) return null;

        var goalCategories = new Dictionary<string, int>();
        var outcomes = new Dictionary<string, int>();
        var sessionTypes = new Dictionary<string, int>();
        var briefSummaries = new List<string>();

        foreach (var facet in facets)
        {
            foreach (var (cat, count) in facet.GoalCategories)
            {
                if (!goalCategories.TryAdd(cat, count))
                {
                    goalCategories[cat] += count;
                }
            }

            if (!string.IsNullOrEmpty(facet.Outcome) && !outcomes.TryAdd(facet.Outcome, 1))
            {
                outcomes[facet.Outcome]++;
            }

            if (!string.IsNullOrEmpty(facet.SessionType) && !sessionTypes.TryAdd(facet.SessionType, 1))
            {
                sessionTypes[facet.SessionType]++;
            }

            if (!string.IsNullOrEmpty(facet.BriefSummary))
            {
                briefSummaries.Add(facet.BriefSummary);
            }
        }

        return new FacetSummary
        {
            Total = facets.Count,
            GoalCategories = goalCategories,
            Outcomes = outcomes,
            SessionTypes = sessionTypes,
            BriefSummaries = briefSummaries
        };
    }
}

/// <summary>
/// Multi-Clauding 检测器 — CLI 简化版
/// </summary>
public static class MultiClaudingDetector
{
    public static MultiClaudingResult Detect(IReadOnlyList<InsightSessionMeta> sessions)
    {
        // 简化检测：查找时间重叠的会话
        var overlapEvents = 0;
        var sessionsInvolved = 0;
        var userMessagesDuring = 0;

        if (sessions.Count < 2)
        {
            return new MultiClaudingResult
            {
                OverlapEvents = overlapEvents,
                SessionsInvolved = sessionsInvolved,
                UserMessagesDuring = userMessagesDuring
            };
        }

        var sorted = sessions.OrderBy(s => s.StartTime).ToList();
        for (var i = 0; i < sorted.Count - 1; i++)
        {
            var end1 = sorted[i].StartTime.AddMinutes(sorted[i].DurationMinutes);
            if (end1 > sorted[i + 1].StartTime)
            {
                overlapEvents++;
                sessionsInvolved += 2;
                userMessagesDuring += sorted[i].UserMessageCount + sorted[i + 1].UserMessageCount;
            }
        }

        return new MultiClaudingResult
        {
            OverlapEvents = overlapEvents,
            SessionsInvolved = sessionsInvolved,
            UserMessagesDuring = userMessagesDuring
        };
    }
}

/// <summary>
/// 洞察提示词构建器 — CLI 简化版
/// </summary>
public static class InsightPrompts
{
    public static string BuildInsightDataContext(AggregatedInsightData aggregated, FacetSummary? facetSummary, MultiClaudingResult multiClauding)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Sessions: {aggregated.TotalSessions}");
        sb.AppendLine($"Messages: {aggregated.TotalMessages}");
        sb.AppendLine($"Input Tokens: {aggregated.TotalInputTokens}");
        sb.AppendLine($"Output Tokens: {aggregated.TotalOutputTokens}");

        if (facetSummary is not null)
        {
            sb.AppendLine($"Facets: {facetSummary.Total}");
        }

        if (multiClauding.OverlapEvents > 0)
        {
            sb.AppendLine($"Multi-Clauding detected: {multiClauding.OverlapEvents} overlap events");
        }

        return sb.ToString();
    }

    public static string BuildFacetExtractionPrompt(string transcriptText)
    {
        return $"请分析以下对话记录，提取关键主题、技术栈和模式:\n\n{transcriptText}";
    }

    public static string BuildTranscriptSummaryPrompt(string chunk)
    {
        return $"请总结以下对话片段的关键信息:\n\n{chunk}";
    }
}

/// <summary>
/// 洞察 HTML 报告生成器 — CLI 简化版
/// </summary>
public static class InsightHtmlReport
{
    public static string Generate(AggregatedInsightData aggregated, FacetSummary? facetSummary, MultiClaudingResult multiClauding, string insightsText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset='utf-8'><title>JoinCode Insights Report</title></head><body>");
        sb.AppendLine("<h1>JoinCode Insights Report</h1>");
        sb.AppendLine($"<p>Sessions: {aggregated.TotalSessions}</p>");
        sb.AppendLine($"<p>Messages: {aggregated.TotalMessages:N0}</p>");
        sb.AppendLine($"<p>Input Tokens: {aggregated.TotalInputTokens:N0}</p>");
        sb.AppendLine($"<p>Output Tokens: {aggregated.TotalOutputTokens:N0}</p>");

        if (!string.IsNullOrEmpty(insightsText))
        {
            sb.AppendLine("<h2>AI Insights</h2>");
            sb.AppendLine($"<pre>{insightsText}</pre>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}
