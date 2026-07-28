
namespace Core.Memdir.ToolHandlers;

/// <summary>
/// 记忆扩展工具处理器 - 提供助手日志、搜索历史和团队记忆同步功能
/// </summary>
[McpToolHandler(ToolCategory.Memory, Optional = true)]
public class MemoryExtensionToolHandlers
{
    private readonly IMemoryManagementService _memoryManagementService;
    private readonly global::Memdir.Sync.ITeamMemorySyncService? _teamMemorySyncService;
    private readonly IClockService _clock;

    public MemoryExtensionToolHandlers(
        IMemoryManagementService memoryManagementService,
        global::Memdir.Sync.ITeamMemorySyncService? teamMemorySyncService = null,
        IClockService? clock = null)
    {
        _memoryManagementService = memoryManagementService ?? throw new ArgumentNullException(nameof(memoryManagementService));
        _teamMemorySyncService = teamMemorySyncService;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 追加条目到助手日志
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryDailyLogAppend, "Append an entry to the assistant daily log", "memory")]
    public async Task<ToolResult> MemoryDailyLogAppendAsync(
        [McpToolParameter("Log content")] string content,
        [McpToolParameter("Log category (Action/Observation/Decision/Result, optional)", Required = false)] string? category = null,
        [McpToolParameter("Related memory ID (optional)", Required = false)] string? related_memory_id = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VaultContentCannotBeEmpty)).Build();
        }

        var categoryValue = DailyLogCategoryExtensions.FromValue(category) ?? DailyLogCategory.Observation;

        var entry = await _memoryManagementService.AppendDailyLogEntryAsync(
            content, categoryValue, related_memory_id, cancellationToken).ConfigureAwait(false);

        var response = new StringBuilder();
        response.AppendLine($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.VaultLogEntryAppended)}");
        response.AppendLine(L.T(StringKey.VaultLabelCategory, categoryValue.GetLabel()));
        response.AppendLine(L.T(StringKey.VaultLabelContent, entry.Content));

        if (!string.IsNullOrEmpty(entry.RelatedMemoryId))
        {
            response.AppendLine(L.T(StringKey.VaultLabelRelatedMemory, entry.RelatedMemoryId));
        }

        response.AppendLine(L.T(StringKey.VaultLabelTime, entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")));

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取今日助手日志
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryDailyLogGet, "Get today's assistant daily log", "memory")]
    public async Task<ToolResult> MemoryDailyLogGetAsync(
        CancellationToken cancellationToken = default)
    {
        var dailyLogPrompt = await _memoryManagementService.BuildDailyLogPromptAsync(
            ct: cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(dailyLogPrompt))
        {
            return McpResultBuilder.Success()
                .WithText(L.T(StringKey.VaultNoDailyLogToday, ObjectSymbol.DiamondOpen.ToValue()))
                .Build();
        }

        return McpResultBuilder.Success()
            .WithText($"{L.T(StringKey.VaultDailyLogToday, ObjectSymbol.DiamondOpen.ToValue())}{Environment.NewLine}{Environment.NewLine}{dailyLogPrompt}")
            .Build();
    }

    /// <summary>
    /// 搜索过往对话记忆
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemorySearchHistory, "Search past conversation memories", "memory")]
    public async Task<ToolResult> MemorySearchHistoryAsync(
        [McpToolParameter("Search query")] string query,
        [McpToolParameter("Result count limit", Required = false, DefaultValue = "10")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VaultQueryCannotBeEmpty)).Build();
        }

        var results = await _memoryManagementService.SearchPastConversationsAsync(
            query, limit ?? 10, cancellationToken).ConfigureAwait(false);

        var response = new StringBuilder();
        response.AppendLine($"{ObjectSymbol.Search.ToValue()} {L.T(StringKey.VaultSearchHistoryResult)}");
        response.AppendLine(L.T(StringKey.VaultLabelQuery, query));
        response.AppendLine(L.T(StringKey.VaultFoundRelevantMemories, results.Count, results.Count));
        response.AppendLine();

        if (results.Count == 0)
        {
            response.AppendLine(L.T(StringKey.VaultNoPastConversationMemories));
        }
        else
        {
            for (int i = 0; i < results.Count; i++)
            {
                var memory = results[i];
                var ageDays = (_clock.GetUtcNow() - memory.CreatedAt).Days;
                var ageLabel = ageDays switch
                {
                    0 => L.T(StringKey.VaultToday),
                    1 => L.T(StringKey.VaultYesterday),
                    < 7 => L.T(StringKey.VaultDaysAgo, ageDays),
                    < 30 => L.T(StringKey.VaultWeeksAgo, ageDays / 7),
                    _ => L.T(StringKey.VaultMonthsAgo, ageDays / 30)
                };

                response.AppendLine($"{i + 1}. [{memory.Type.GetName()}] {memory.Title ?? L.T(StringKey.VaultNoTitle)} ({ageLabel})");
                response.AppendLine(L.T(StringKey.VaultLabelContent, memory.Content[..Math.Min(100, memory.Content.Length)]));
                response.AppendLine(L.T(StringKey.VaultLabelIdAccess, memory.Id, memory.AccessCount));

                if (!memory.Tags.IsEmpty)
                {
                    response.AppendLine(L.T(StringKey.VaultLabelTags, string.Join(", ", memory.Tags.Take(5))));
                }

                response.AppendLine();
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 同步团队记忆
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryTeamSync, "Sync team memories", "memory")]
    public async Task<ToolResult> MemoryTeamSyncAsync(
        [McpToolParameter("Team ID")] string team_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(team_id))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.VaultTeamIdCannotBeEmpty)).Build();
        }

        if (_teamMemorySyncService is null)
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.VaultTeamSyncServiceNotRegistered))
                .Build();
        }

        var status = await _teamMemorySyncService.SyncTeamMemoryAsync(team_id, cancellationToken).ConfigureAwait(false);

        var response = new StringBuilder();
        response.AppendLine(L.T(StringKey.VaultTeamSyncComplete, StatusSymbol.Tick.ToValue()));
        response.AppendLine(L.T(StringKey.VaultLabelTeam, status.TeamId));
        response.AppendLine(L.T(StringKey.VaultLabelSyncTime, status.LastSyncAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? L.T(StringKey.VaultNeverSynced)));
        response.AppendLine(L.T(StringKey.VaultLabelSyncedCount, status.SyncedMemoryCount));
        response.AppendLine(L.T(StringKey.VaultLabelWatching, status.IsWatching ? L.T(StringKey.VaultYes) : L.T(StringKey.VaultNo)));

        if (status.HasConflicts)
        {
            response.AppendLine();
            response.AppendLine(L.T(StringKey.VaultConflictCount, StatusSymbol.Warning.ToValue(), status.Conflicts.Count));

            foreach (var conflict in status.Conflicts.Take(10))
            {
                var conflictType = conflict.ConflictType switch
                {
                    ConflictType.ContentMismatch => L.T(StringKey.VaultConflictContentMismatch),
                    ConflictType.DeletedLocally => L.T(StringKey.VaultConflictDeletedLocally),
                    ConflictType.DeletedRemotely => L.T(StringKey.VaultConflictDeletedRemotely),
                    _ => conflict.ConflictType.ToString()
                };

                response.AppendLine(L.T(StringKey.VaultConflictMemoryId, conflictType, conflict.MemoryId));
            }

            if (status.Conflicts.Count > 10)
            {
                response.AppendLine(L.T(StringKey.VaultMoreConflicts, status.Conflicts.Count - 10));
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取团队同步状态
    /// </summary>
    [McpTool(MemoryToolNameConstants.MemoryTeamStatus, "Get team sync status", "memory")]
    public Task<ToolResult> MemoryTeamStatusAsync(
        [McpToolParameter("Team ID")] string team_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(team_id))
        {
            return Task.FromResult(McpResultBuilder.Error().WithText(L.T(StringKey.VaultTeamIdCannotBeEmpty)).Build());
        }

        if (_teamMemorySyncService is null)
        {
            return Task.FromResult(McpResultBuilder.Error()
                .WithText(L.T(StringKey.VaultTeamSyncServiceNotRegisteredStatus))
                .Build());
        }

        var status = _teamMemorySyncService.GetSyncStatus(team_id);

        if (status is null)
        {
            return Task.FromResult(McpResultBuilder.Success()
                .WithText(L.T(StringKey.VaultTeamNeverSynced, team_id))
                .Build());
        }

        var response = new StringBuilder();
        response.AppendLine(L.T(StringKey.VaultTeamSyncStatus, PrioritySymbol.Critical.ToValue()));
        response.AppendLine(L.T(StringKey.VaultLabelTeam, status.TeamId));
        response.AppendLine(L.T(StringKey.VaultLabelLastSync, status.LastSyncAt.HasValue ? status.LastSyncAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : L.T(StringKey.VaultNeverSynced)));
        response.AppendLine(L.T(StringKey.VaultLabelWatching, status.IsWatching ? L.T(StringKey.VaultYes) : L.T(StringKey.VaultNo)));
        response.AppendLine(L.T(StringKey.VaultLabelSyncedMemories, status.SyncedMemoryCount));
        response.AppendLine(L.T(StringKey.VaultLabelHasConflicts, status.HasConflicts ? $"{StatusSymbol.Warning.ToValue()} {L.T(StringKey.VaultYes)} ({status.Conflicts.Count})" : L.T(StringKey.VaultNo)));

        return Task.FromResult(McpResultBuilder.Success().WithText(response.ToString()).Build());
    }
}
