
namespace Core.Memdir.ToolHandlers;

/// <summary>
/// 内存管理工具处理器 - 提供内存扫描、年龄管理和团队内存功能
/// </summary>
[McpToolHandler(ToolCategory.Memory, Optional = true)]
public class MemoryManagementToolHandlers
{
    private readonly IMemoryManagementService _memoryManagementService;

    public MemoryManagementToolHandlers(IMemoryManagementService memoryManagementService)
    {
        _memoryManagementService = memoryManagementService ?? throw new ArgumentNullException(nameof(memoryManagementService));
    }

    /// <summary>
    /// 扫描内存，查找相关记忆
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryScan, "Scan memories to find relevant ones", "memory")]
    public async Task<ToolResult> MemoryScanAsync(
        [McpToolParameter("Search query")] string query,
        [McpToolParameter("Category filter (optional)", Required = false)] string? category = null,
        [McpToolParameter("Result count limit", Required = false, DefaultValue = "10")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VaultQueryCannotBeEmpty)).Build();
        }

        var result = await _memoryManagementService.ScanMemoriesAsync(query, category, limit ?? 10, cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.Search.ToValue()} {L.T(StringKey.VaultMemoryScanResult)}");
        response.AppendLine(L.T(StringKey.VaultLabelQuery, query));
        response.AppendLine(L.T(StringKey.VaultFoundRelevantMemories, result.RelevantMemories.Count, result.TotalMemories));
        response.AppendLine();

        if (result.RelevantMemories.Count == 0)
        {
            response.AppendLine(L.T(StringKey.VaultNoRelevantMemories));
        }
        else
        {
            for (int i = 0; i < result.RelevantMemories.Count; i++)
            {
                var scored = result.RelevantMemories[i];
                var memory = scored.Memory;

                response.AppendLine(L.T(StringKey.VaultLabelScore, i + 1, memory.Id, scored.RelevanceScore));
                response.AppendLine(L.T(StringKey.VaultLabelContent, memory.Content[..Math.Min(100, memory.Content.Length)]));

                if (!string.IsNullOrEmpty(scored.MatchReason))
                {
                    response.AppendLine(L.T(StringKey.VaultLabelMatch, scored.MatchReason));
                }

                response.AppendLine(L.T(StringKey.VaultLabelTypeAccess, memory.Type, memory.AccessCount));
                response.AppendLine();
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取内存年龄信息
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryAge, "Get memory age and access statistics", "memory")]
    public async Task<ToolResult> MemoryAgeAsync(
        [McpToolParameter("Show only memories needing attention", Required = false, DefaultValue = "false")] bool? attention_only = null,
        CancellationToken cancellationToken = default)
    {
        var ageInfos = await _memoryManagementService.GetMemoryAgeInfoAsync(cancellationToken).ConfigureAwait(false);

        if (attention_only == true)
        {
            ageInfos = ageInfos.Where(a => a.ShouldArchive || a.ShouldDelete).ToList();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.DiamondOpen.ToValue()} {L.T(StringKey.VaultMemoryAgeInfo)}");
        response.AppendLine(L.T(StringKey.VaultTotalMemories, ageInfos.Count));
        response.AppendLine();

        if (ageInfos.Count == 0)
        {
            response.AppendLine(L.T(StringKey.VaultNoMemories));
        }
        else
        {
            // 按健康分数排序
            var sorted = ageInfos.OrderBy(a => a.HealthScore).ToList();

            foreach (var info in sorted)
            {
                var statusIcon = info.ShouldDelete ? ObjectSymbol.Clean.ToValue() :
                                info.ShouldArchive ? ObjectSymbol.DiamondFilled.ToValue() :
                                info.HealthScore < 30 ? StatusSymbol.Warning.ToValue() : StatusSymbol.Tick.ToValue();

                response.AppendLine($"{statusIcon} [{info.MemoryId}]");
                response.AppendLine(L.T(StringKey.VaultLabelAge, info.AgeInDays));
                response.AppendLine(L.T(StringKey.VaultLabelUnaccessed, info.DaysSinceLastAccess));
                response.AppendLine(L.T(StringKey.VaultLabelAccessCount, info.AccessCount));
                response.AppendLine(L.T(StringKey.VaultLabelHealthScore, info.HealthScore));

                if (info.ShouldDelete)
                {
                    response.AppendLine(L.T(StringKey.VaultSuggestDelete, StatusSymbol.Warning.ToValue()));
                }
                else if (info.ShouldArchive)
                {
                    response.AppendLine(L.T(StringKey.VaultSuggestArchive, ObjectSymbol.DiamondFilled.ToValue()));
                }

                response.AppendLine();
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 执行内存清理
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryCleanup, "Clean up old memories (archive or delete)", "memory")]
    public async Task<ToolResult> MemoryCleanupAsync(
        [McpToolParameter("Archive threshold in days (default 90)", Required = false)] int? archive_after_days = null,
        [McpToolParameter("Delete threshold in days (default 180)", Required = false)] int? delete_after_days = null,
        [McpToolParameter("Confirm execution (enter 'yes' to confirm)")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (confirm != "yes")
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.VaultCleanupConfirmRequired))
                .Build();
        }

        var result = await _memoryManagementService.CleanupOldMemoriesAsync(
            archive_after_days,
            delete_after_days,
            cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.Clean.ToValue()} {L.T(StringKey.VaultMemoryCleanupComplete)}");
        response.AppendLine();
        response.AppendLine(L.T(StringKey.VaultCheckedMemories, result.CheckedCount));
        response.AppendLine(L.T(StringKey.VaultArchivedMemories, result.ArchivedCount));
        response.AppendLine(L.T(StringKey.VaultDeletedMemories, result.DeletedCount));
        response.AppendLine(L.T(StringKey.VaultRetainedMemories, result.RetainedCount));

        if (result.ProcessedIds.Count > 0)
        {
            response.AppendLine();
            response.AppendLine(L.T(StringKey.VaultProcessedMemoryIds));
            foreach (var id in result.ProcessedIds.Take(10))
            {
                response.AppendLine($"  - {id}");
            }

            if (result.ProcessedIds.Count > 10)
            {
                response.AppendLine(L.T(StringKey.VaultMoreItems, result.ProcessedIds.Count - 10));
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取内存健康报告
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryHealth, "Get memory health report", "memory")]
    public async Task<ToolResult> MemoryHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var report = await _memoryManagementService.GetHealthReportAsync(cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.Health.ToValue()} {L.T(StringKey.VaultMemoryHealthReport)}");
        response.AppendLine();
        response.AppendLine(L.T(StringKey.VaultTotalMemoryCount, report.TotalMemories));
        response.AppendLine(L.T(StringKey.VaultHealthyMemories, report.HealthyMemories, GetPercentage(report.HealthyMemories, report.TotalMemories)));
        response.AppendLine(L.T(StringKey.VaultNeedsAttention, report.NeedsAttention, GetPercentage(report.NeedsAttention, report.TotalMemories)));
        response.AppendLine(L.T(StringKey.VaultSuggestArchiveCount, report.ShouldArchive));
        response.AppendLine(L.T(StringKey.VaultSuggestDeleteCount, report.ShouldDelete));
        response.AppendLine(L.T(StringKey.VaultAvgHealthScore, report.AverageHealthScore));

        if (report.AgeDistribution.Count > 0)
        {
            response.AppendLine();
            response.AppendLine($"{ObjectSymbol.List.ToValue()} {L.T(StringKey.VaultAgeDistribution)}");
            foreach (var (range, count) in report.AgeDistribution)
            {
                var bar = new string('█', count > 0 ? Math.Max(1, count * 20 / report.TotalMemories) : 0);
                response.AppendLine($"  {range,-10} {bar} {count}");
            }
        }

        if (report.ShouldArchive > 0 || report.ShouldDelete > 0)
        {
            response.AppendLine();
            response.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} {L.T(StringKey.VaultSuggestions)}");
            if (report.ShouldDelete > 0)
            {
                response.AppendLine(L.T(StringKey.VaultSuggestDeleteUseCleanup, report.ShouldDelete));
            }
            if (report.ShouldArchive > 0)
            {
                response.AppendLine(L.T(StringKey.VaultSuggestArchiveCount2, report.ShouldArchive));
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 添加团队内存路径
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryAddTeamPath, "Add a team shared memory path", "memory")]
    public async Task<ToolResult> MemoryAddTeamPathAsync(
        [McpToolParameter("Team ID")] string team_id,
        [McpToolParameter("Memory path")] string path,
        [McpToolParameter("Whether shared", Required = false, DefaultValue = "true")] bool? is_shared = null,
        [McpToolParameter("Allowed agents list (comma-separated)", Required = false)] string? allowed_agents = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(team_id))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VaultTeamIdCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VaultPathCannotBeEmpty)).Build();
        }

        var agents = !string.IsNullOrEmpty(allowed_agents)
            ? allowed_agents.Split(',').Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)).ToList()
            : null;

        await _memoryManagementService.AddTeamMemoryPathAsync(team_id, path, is_shared ?? true, agents, cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.VaultTeamMemoryPathAdded)}");
        response.AppendLine();
        response.AppendLine(L.T(StringKey.VaultLabelTeam, team_id));
        response.AppendLine(L.T(StringKey.VaultLabelPath, path));
        response.AppendLine(L.T(StringKey.VaultLabelShared, is_shared ?? true ? L.T(StringKey.VaultYes) : L.T(StringKey.VaultNo)));

        if (agents?.Count > 0)
        {
            response.AppendLine(L.T(StringKey.VaultLabelAllowedAgents, string.Join(", ", agents)));
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 列出团队内存路径
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryListTeamPaths, "List team memory paths", "memory")]
    public async Task<ToolResult> MemoryListTeamPathsAsync(
        [McpToolParameter("Team ID (optional, omit to show all)", Required = false)] string? team_id = null,
        CancellationToken cancellationToken = default)
    {
        var paths = await _memoryManagementService.GetTeamMemoryPathsAsync(team_id, cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{PrioritySymbol.Critical.ToValue()} {L.T(StringKey.VaultTeamMemoryPaths)}");

        if (!string.IsNullOrEmpty(team_id))
        {
            response.AppendLine(L.T(StringKey.VaultLabelTeam, team_id));
        }

        response.AppendLine(L.T(StringKey.VaultPathCount, paths.Count));
        response.AppendLine();

        if (paths.Count == 0)
        {
            response.AppendLine(L.T(StringKey.VaultNoTeamMemoryPaths));
        }
        else
        {
            var grouped = paths.GroupBy(p => p.TeamId);

            foreach (var group in grouped)
            {
                response.AppendLine($"{ObjectSymbol.Directory.ToValue()} {L.T(StringKey.VaultLabelTeam, group.Key)}");

                foreach (var path in group)
                {
                    var shareIcon = path.IsShared ? StatusSymbol.Circle.ToValue() : ObjectSymbol.DiamondFilled.ToValue();
                    response.AppendLine($"  {shareIcon} {path.Path}");

                    if (path.AllowedAgents.Count > 0)
                    {
                        response.AppendLine($"     {L.T(StringKey.VaultLabelAllowedAgents, string.Join(", ", path.AllowedAgents))}");
                    }
                }

                response.AppendLine();
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 移除团队内存路径
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryRemoveTeamPath, "Remove a team memory path", "memory")]
    public async Task<ToolResult> MemoryRemoveTeamPathAsync(
        [McpToolParameter("Team ID")] string team_id,
        [McpToolParameter("Memory path")] string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(team_id))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VaultTeamIdCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VaultPathCannotBeEmpty)).Build();
        }

        var removed = await _memoryManagementService.RemoveTeamMemoryPathAsync(team_id, path, cancellationToken).ConfigureAwait(false);

        if (!removed)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VaultTeamNotFound)).Build();
        }

        return McpResultBuilder.Success()
            .WithText(L.T(StringKey.VaultTeamPathRemoved, StatusSymbol.Tick.ToValue(), team_id, path))
            .Build();
    }

    /// <summary>
    /// 扫描团队共享记忆
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryScanTeam, "Scan team shared memories", "memory")]
    public async Task<ToolResult> MemoryScanTeamAsync(
        [McpToolParameter("Team ID")] string team_id,
        [McpToolParameter("Search query")] string query,
        [McpToolParameter("Result count limit", Required = false, DefaultValue = "10")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(team_id))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VaultTeamIdCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VaultQueryCannotBeEmpty)).Build();
        }

        var result = await _memoryManagementService.ScanTeamMemoriesAsync(team_id, query, limit ?? 10, cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.VaultTeamSharedMemoryScan, PrioritySymbol.Critical.ToValue(), team_id));
        response.AppendLine(L.T(StringKey.VaultLabelQuery, query));
        response.AppendLine(L.T(StringKey.VaultFoundRelevantMemories, result.RelevantMemories.Count, result.TotalMemories));
        response.AppendLine();

        if (result.RelevantMemories.Count == 0)
        {
            response.AppendLine(L.T(StringKey.VaultNoRelevantMemories));
        }
        else
        {
            for (int i = 0; i < result.RelevantMemories.Count; i++)
            {
                var scored = result.RelevantMemories[i];
                var memory = scored.Memory;

                response.AppendLine(L.T(StringKey.VaultLabelScore, i + 1, memory.Id, scored.RelevanceScore));
                response.AppendLine(L.T(StringKey.VaultLabelContent, memory.Content[..Math.Min(100, memory.Content.Length)]));
                response.AppendLine(L.T(StringKey.VaultLabelSource, memory.Source));
                response.AppendLine();
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #region Private Methods

    private static double GetPercentage(int part, int total)
    {
        return total > 0 ? (double)part / total * 100 : 0;
    }

    #endregion
}
