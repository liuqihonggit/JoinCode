namespace JoinCode.ChatCommands;

/// <summary>
/// /stats 命令 — 对齐 TS stats.tsx OverviewTab
/// TS 是全屏交互式 TUI（React），C# 是终端文本输出
/// 对齐内容：活动热力图、Streaks、PeakActivity、FunFactoid、FavoriteModel、日期范围
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Stats, Description = "查看会话统计", Usage = "/stats [--today|--total|--7d|--30d|--all|--session]", Category = ChatCommandCategory.Info)]
public sealed class StatsCommand : IChatCommand
{
    private readonly IClockService _clock = SystemClockService.Instance;
    public string Name => ChatCommandNameConstants.Stats;
    public string Description => "查看会话统计";
    public string Usage => "/stats [--today|--total|--7d|--30d|--all|--session]";
    public string[] Aliases => ["stat"];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var scope = args.Length > 0 ? args[0].ToLowerInvariant().TrimStart('-') : "today";

        // 跨会话统计模式 — 对齐 TS Stats.tsx aggregateClaudeCodeStatsForRange
        if (scope is "7d" or "30d" or "all")
        {
            await ShowCrossSessionStatsAsync(context, scope).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        // 回退到 UsageTracker 模式
        var usageTracker = context.Services!.UsageTracker;

        if (usageTracker is null)
        {
            var fallbackData = new StatsData
            {
                TotalSessions = 1,
                ActiveDays = 1,
            };
            TerminalHelper.WriteLine(new StatsRenderer().Render(fallbackData));
            TerminalHelper.WriteLine($"{TerminalColors.Muted}  (使用量追踪服务不可用，显示默认数据){AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        TokenUsageStatistics stats = scope switch
        {
            "total" => usageTracker.GetTotalStatistics(),
            "session" => usageTracker.GetSessionStatistics(context.SessionId),
            _ => usageTracker.GetTodayStatistics()
        };

        if (stats is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}  暂无统计数据{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var data = MapToStatsData(stats, context);

        TerminalHelper.WriteLine(new StatsRenderer().Render(data));

        if (stats is not null && (stats.TotalCacheCreationTokens > 0 || stats.TotalCacheReadTokens > 0))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}  缓存创建: {stats.TotalCacheCreationTokens:N0}, 缓存读取: {stats.TotalCacheReadTokens:N0}{AnsiStyleConstants.Reset}");
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 跨会话统计 — 对齐 TS Stats.tsx OverviewTab
    /// 包含：活动热力图、Streaks、PeakActivity、FunFactoid、FavoriteModel
    /// </summary>
    private async Task ShowCrossSessionStatsAsync(ChatCommandContext context, string scope)
    {
        var scanner = ChatCommandBase.GetService<IInsightSessionScanner>(context);
        if (scanner is null)
        {
            TerminalHelper.WriteLine("会话扫描服务不可用，无法获取跨会话统计。");
            TerminalHelper.WriteLine("使用 /stats --today 查看今日统计。");
            return;
        }

        TerminalHelper.WriteLine("正在扫描会话文件...");
        TerminalHelper.NewLine();

        try
        {
            var sessions = await scanner.ScanAllSessionsAsync(context.CancellationToken).ConfigureAwait(false);

            if (sessions.Count == 0)
            {
                TerminalHelper.WriteLine("未找到会话记录。开始使用以解锁统计数据！");
                return;
            }

            // 按日期范围过滤 — 对齐 TS StatsDateRange
            var filtered = FilterByDateRange(sessions, scope);
            var aggregated = InsightDataAggregator.Aggregate(filtered);

            // 渲染输出 — 对齐 TS OverviewTab
            await RenderOverviewAsync(aggregated, scope).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TerminalHelper.WriteLine("扫描已取消。");
        }
        catch (Exception ex)
        {
            TerminalHelper.WriteLine($"扫描会话失败: {ex.Message}");
            TerminalHelper.WriteLine("使用 /stats --today 查看今日统计。");
        }
    }

    /// <summary>
    /// 按日期范围过滤会话 — 对齐 TS StatsDateRange: 7d/30d/all
    /// </summary>
    private IReadOnlyList<InsightSessionMeta> FilterByDateRange(IReadOnlyList<InsightSessionMeta> sessions, string scope)
    {
        if (scope == "all") return sessions;

        var cutoff = scope switch
        {
            "7d" => _clock.GetUtcNow().AddDays(-7),
            "30d" => _clock.GetUtcNow().AddDays(-30),
            _ => DateTime.MinValue
        };

        if (cutoff == DateTime.MinValue) return sessions;

        var result = new List<InsightSessionMeta>();
        foreach (var s in sessions)
        {
            if (s.StartTime != default && s.StartTime >= cutoff)
            {
                result.Add(s);
            }
        }

        return result;
    }

    /// <summary>
    /// 渲染 Overview — 对齐 TS Stats.tsx OverviewTab 布局
    /// </summary>
    private async Task RenderOverviewAsync(AggregatedInsightData data, string scope)
    {
        var rangeLabel = scope switch
        {
            "7d" => "Last 7 days",
            "30d" => "Last 30 days",
            "all" => "All time",
            _ => scope
        };

        // 概览 Tab
        var overviewContent = new StringBuilder();
        overviewContent.AppendLine($"{AnsiStyleConstants.Bold}Stats — {rangeLabel}{AnsiStyleConstants.Reset}");
        overviewContent.AppendLine();

        if (data.DailyActivities.Count > 0)
        {
            var heatmap = TerminalCharts.ActivityHeatmap(data.DailyActivities, title: "Activity Heatmap");
            overviewContent.AppendLine(heatmap);
            overviewContent.AppendLine();
        }

        if (!string.IsNullOrEmpty(data.FavoriteModel))
        {
            overviewContent.Append(TerminalColors.Muted);
            overviewContent.Append("  Favorite model: ");
            overviewContent.Append(AnsiStyleConstants.Reset);
            overviewContent.AppendLine(data.FavoriteModel);
        }

        overviewContent.Append(TerminalColors.Muted);
        overviewContent.Append("  Sessions: ");
        overviewContent.Append(AnsiStyleConstants.Reset);
        overviewContent.AppendLine(NumberFormatter.FormatCompact(data.TotalSessions));

        if (data.TotalDurationHours > 0)
        {
            overviewContent.Append(TerminalColors.Muted);
            overviewContent.Append("  Total duration: ");
            overviewContent.Append(AnsiStyleConstants.Reset);
            overviewContent.AppendLine($"{data.TotalDurationHours:F1}h");
        }

        var rangeDays = scope switch
        {
            "7d" => 7,
            "30d" => 30,
            _ => data.DaysActive
        };

        overviewContent.Append(TerminalColors.Muted);
        overviewContent.Append("  Active days: ");
        overviewContent.Append(AnsiStyleConstants.Reset);
        overviewContent.Append(data.DaysActive.ToString());
        overviewContent.AppendLine($"{TerminalColors.Muted}/{rangeDays}{AnsiStyleConstants.Reset}");

        overviewContent.Append(TerminalColors.Muted);
        overviewContent.Append("  Longest streak: ");
        overviewContent.Append(AnsiStyleConstants.Reset);
        overviewContent.Append($"{AnsiStyleConstants.Bold}{data.LongestStreak}{AnsiStyleConstants.Reset}");
        overviewContent.AppendLine(data.LongestStreak == 1 ? " day" : " days");

        if (data.PeakActivityDay.HasValue)
        {
            overviewContent.Append(TerminalColors.Muted);
            overviewContent.Append("  Most active day: ");
            overviewContent.Append(AnsiStyleConstants.Reset);
            overviewContent.AppendLine(data.PeakActivityDay.Value.ToString("MMM dd"));
        }

        overviewContent.Append(TerminalColors.Muted);
        overviewContent.Append("  Current streak: ");
        overviewContent.Append(AnsiStyleConstants.Reset);
        overviewContent.Append($"{AnsiStyleConstants.Bold}{data.CurrentStreak}{AnsiStyleConstants.Reset}");
        overviewContent.AppendLine(data.CurrentStreak == 1 ? " day" : " days");

        if (data.PeakActivityHour > 0)
        {
            overviewContent.Append(TerminalColors.Muted);
            overviewContent.Append("  Peak activity hour: ");
            overviewContent.Append(AnsiStyleConstants.Reset);
            overviewContent.AppendLine($"{data.PeakActivityHour}:00");
        }

        var factoid = TerminalCharts.FunFactoid(data.TotalInputTokens + data.TotalOutputTokens, data.DaysActive, data.TotalDurationHours);
        if (!string.IsNullOrEmpty(factoid))
        {
            overviewContent.AppendLine();
            overviewContent.AppendLine($"{TerminalColors.Accent}  {factoid}{AnsiStyleConstants.Reset}");
        }

        // Token 用量 Tab
        var tokenContent = new StringBuilder();
        tokenContent.AppendLine($"{AnsiStyleConstants.Bold}Token Usage{AnsiStyleConstants.Reset}");
        tokenContent.AppendLine();
        var totalTokens = data.TotalInputTokens + data.TotalOutputTokens;
        tokenContent.Append(TerminalColors.Muted);
        tokenContent.Append("  Total: ");
        tokenContent.Append(AnsiStyleConstants.Reset);
        tokenContent.AppendLine(NumberFormatter.FormatCompact(totalTokens));
        tokenContent.Append(TerminalColors.Muted);
        tokenContent.Append("  Input: ");
        tokenContent.Append(AnsiStyleConstants.Reset);
        tokenContent.AppendLine(NumberFormatter.FormatCompact(data.TotalInputTokens));
        tokenContent.Append(TerminalColors.Muted);
        tokenContent.Append("  Output: ");
        tokenContent.Append(AnsiStyleConstants.Reset);
        tokenContent.AppendLine(NumberFormatter.FormatCompact(data.TotalOutputTokens));
        if (data.TotalCostUsd > 0)
        {
            tokenContent.Append(TerminalColors.Muted);
            tokenContent.Append("  Estimated cost: ");
            tokenContent.Append(AnsiStyleConstants.Reset);
            tokenContent.AppendLine($"${data.TotalCostUsd:F4}");
        }

        if (data.GitCommits > 0 || data.GitPushes > 0)
        {
            tokenContent.AppendLine();
            tokenContent.AppendLine($"{AnsiStyleConstants.Bold}Git{AnsiStyleConstants.Reset}");
            tokenContent.Append(TerminalColors.Muted);
            tokenContent.Append("  Commits: ");
            tokenContent.Append(AnsiStyleConstants.Reset);
            tokenContent.AppendLine(data.GitCommits.ToString());
            tokenContent.Append(TerminalColors.Muted);
            tokenContent.Append("  Pushes: ");
            tokenContent.Append(AnsiStyleConstants.Reset);
            tokenContent.AppendLine(data.GitPushes.ToString());
        }

        if (data.TotalLinesAdded > 0 || data.TotalLinesRemoved > 0)
        {
            tokenContent.AppendLine();
            tokenContent.AppendLine($"{AnsiStyleConstants.Bold}Code Changes{AnsiStyleConstants.Reset}");
            tokenContent.Append(TerminalColors.Muted);
            tokenContent.Append("  Lines added: ");
            tokenContent.Append(AnsiStyleConstants.Reset);
            tokenContent.AppendLine(NumberFormatter.FormatCompact(data.TotalLinesAdded));
            tokenContent.Append(TerminalColors.Muted);
            tokenContent.Append("  Lines removed: ");
            tokenContent.Append(AnsiStyleConstants.Reset);
            tokenContent.AppendLine(NumberFormatter.FormatCompact(data.TotalLinesRemoved));
            tokenContent.Append(TerminalColors.Muted);
            tokenContent.Append("  Files modified: ");
            tokenContent.Append(AnsiStyleConstants.Reset);
            tokenContent.AppendLine(data.TotalFilesModified.ToString());
        }

        // 工具使用 Tab
        var toolsContent = new StringBuilder();
        var topTools = data.ToolCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(16)
            .ToList();

        if (topTools.Count > 0)
        {
            toolsContent.AppendLine($"{AnsiStyleConstants.Bold}Top Tools{AnsiStyleConstants.Reset}");
            toolsContent.AppendLine();
            var maxToolCount = topTools.Max(kvp => kvp.Value);
            foreach (var (tool, count) in topTools)
            {
                var barLength = maxToolCount > 0 ? (int)Math.Ceiling((double)count / maxToolCount * 16) : 0;
                var bar = new string('█', barLength);
                toolsContent.Append(TerminalColors.Primary);
                toolsContent.Append($"  {tool,-16} {bar} ");
                toolsContent.Append(AnsiStyleConstants.Reset);
                toolsContent.AppendLine(count.ToString());
            }
        }
        else
        {
            toolsContent.AppendLine("  暂无工具使用数据");
        }

        toolsContent.AppendLine();
        toolsContent.Append($"  {TerminalColors.Muted}使用 /insights deep 获取AI生成的深度洞察{AnsiStyleConstants.Reset}");

        var panel = new TabPanel(
            ["概览", "Token用量", "工具使用"],
            tabIndex => tabIndex switch
            {
                0 => overviewContent.ToString(),
                1 => tokenContent.ToString(),
                2 => toolsContent.ToString(),
                _ => string.Empty
            });

        await panel.ShowAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private StatsData MapToStatsData(TokenUsageStatistics stats, ChatCommandContext context)
    {
        if (stats is null) return new StatsData { TotalSessions = 0, ActiveDays = 0 };

        var data = new StatsData
        {
            TotalSessions = stats.TotalRequests > 0 ? 1 : 0,
            TotalInputTokens = (int)Math.Min(stats.TotalInputTokens, int.MaxValue),
            TotalOutputTokens = (int)Math.Min(stats.TotalOutputTokens, int.MaxValue),
            TotalCostUsd = stats.TotalCostUsd,
            ActiveDays = 1,
            LongestSessionMinutes = (int)(_clock.GetUtcNow() - context.SessionStartedAt).TotalMinutes
        };

        var modelStats = stats.ModelStatistics;
        if (modelStats is not null)
        {
            foreach (var kvp in modelStats)
            {
                var ms = kvp.Value;
                if (ms is null) continue;
                data.ModelBreakdown.Add(new ModelStats(
                    ms.Model,
                    (int)Math.Min(ms.InputTokens, int.MaxValue),
                    (int)Math.Min(ms.OutputTokens, int.MaxValue),
                    ms.EstimatedCostUsd
                ));
            }
        }

        return data;
    }
}
