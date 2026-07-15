
namespace Core.Memdir;

#region Memory Management Models

/// <summary>
/// 记忆年龄信息
/// </summary>
public sealed record MemoryAgeInfo
{
    /// <summary>
    /// 记忆ID
    /// </summary>
    public required string MemoryId { get; init; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// 最后访问时间
    /// </summary>
    public DateTime LastAccessedAt { get; init; }

    /// <summary>
    /// 访问次数
    /// </summary>
    public int AccessCount { get; init; }

    /// <summary>
    /// 记忆年龄（天）
    /// </summary>
    public double AgeInDays => (DateTime.UtcNow - CreatedAt).TotalDays;

    /// <summary>
    /// 未访问天数
    /// </summary>
    public double DaysSinceLastAccess => (DateTime.UtcNow - LastAccessedAt).TotalDays;

    /// <summary>
    /// 记忆健康分数（0-100，越高越重要）
    /// </summary>
    public double HealthScore => CalculateHealthScore();

    /// <summary>
    /// 是否应该归档
    /// </summary>
    public bool ShouldArchive => DaysSinceLastAccess > 90 && AccessCount < 3;

    /// <summary>
    /// 是否应该删除
    /// </summary>
    public bool ShouldDelete => DaysSinceLastAccess > 180 && AccessCount < 2;

    private double CalculateHealthScore()
    {
        // 基于访问频率和新鲜度计算健康分数
        var recencyScore = Math.Exp(-DaysSinceLastAccess / 30.0) * 50; // 30天内访问得高分
        var frequencyScore = Math.Min(AccessCount * 10, 50); // 访问次数得分，最高50
        return recencyScore + frequencyScore;
    }
}

/// <summary>
/// 团队内存路径
/// </summary>
public sealed record TeamMemoryPath
{
    /// <summary>
    /// 团队ID
    /// </summary>
    public required string TeamId { get; init; }

    /// <summary>
    /// 内存路径
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// 是否共享
    /// </summary>
    public bool IsShared { get; init; }

    /// <summary>
    /// 允许访问的代理
    /// </summary>
    public IReadOnlyList<string> AllowedAgents { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 内存扫描结果
/// </summary>
public sealed record MemoryScanResult
{
    /// <summary>
    /// 扫描的记忆数量
    /// </summary>
    public int TotalMemories { get; init; }

    /// <summary>
    /// 相关记忆
    /// </summary>
    public IReadOnlyList<DetailedScoredMemory> RelevantMemories { get; init; } = Array.Empty<DetailedScoredMemory>();

    /// <summary>
    /// 扫描时间
    /// </summary>
    public DateTime ScanTime { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 带分数的记忆
/// </summary>
public sealed record DetailedScoredMemory
{
    public required MemoryEntry Memory { get; init; }
    public double RelevanceScore { get; init; }
    public string? MatchReason { get; init; }
}

/// <summary>
/// 内存清理结果
/// </summary>
public sealed partial class MemoryCleanupResult
{
    /// <summary>
    /// 检查的记忆数量
    /// </summary>
    public int CheckedCount { get; set; }

    /// <summary>
    /// 归档的记忆数量
    /// </summary>
    public int ArchivedCount { get; set; }

    /// <summary>
    /// 删除的记忆数量
    /// </summary>
    public int DeletedCount { get; set; }

    /// <summary>
    /// 保留的记忆数量
    /// </summary>
    public int RetainedCount { get; set; }

    /// <summary>
    /// 被处理的记忆ID
    /// </summary>
    public List<string> ProcessedIds { get; } = new();
}

#endregion

/// <summary>
/// 内存管理服务接口
/// </summary>
public interface IMemoryManagementService : IDisposable
{
    #region Async Methods (Recommended)

    /// <summary>
    /// 异步添加记忆到存储
    /// </summary>
    Task<string> AddMemoryAsync(string content, MemoryType type = MemoryType.User, string? title = null, List<string>? tags = null, string? source = null, CancellationToken ct = default);

    /// <summary>
    /// 异步扫描内存，查找相关记忆
    /// </summary>
    Task<MemoryScanResult> ScanMemoriesAsync(string query, string? category = null, int limit = 10, CancellationToken ct = default);

    /// <summary>
    /// 异步获取记忆年龄信息
    /// </summary>
    Task<List<MemoryAgeInfo>> GetMemoryAgeInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// 异步获取内存健康报告
    /// </summary>
    Task<MemoryHealthReport> GetHealthReportAsync(CancellationToken ct = default);

    /// <summary>
    /// 异步添加团队内存路径
    /// </summary>
    Task AddTeamMemoryPathAsync(string teamId, string path, bool isShared = true, List<string>? allowedAgents = null, CancellationToken ct = default);

    /// <summary>
    /// 异步获取团队内存路径
    /// </summary>
    Task<List<TeamMemoryPath>> GetTeamMemoryPathsAsync(string? teamId = null, CancellationToken ct = default);

    /// <summary>
    /// 异步移除团队内存路径
    /// </summary>
    Task<bool> RemoveTeamMemoryPathAsync(string teamId, string path, CancellationToken ct = default);

    /// <summary>
    /// 异步扫描团队共享记忆
    /// </summary>
    Task<MemoryScanResult> ScanTeamMemoriesAsync(string teamId, string query, int limit = 10, CancellationToken ct = default);

    /// <summary>
    /// 执行内存老化清理
    /// </summary>
    Task<MemoryCleanupResult> CleanupOldMemoriesAsync(
        int? archiveAfterDays = null,
        int? deleteAfterDays = null,
        CancellationToken ct = default);

    /// <summary>
    /// 归档记忆
    /// </summary>
    Task<bool> ArchiveMemoryAsync(string memoryId, CancellationToken ct = default);

    /// <summary>
    /// 恢复归档的记忆
    /// </summary>
    Task<bool> RestoreMemoryAsync(string memoryId, CancellationToken ct = default);

    /// <summary>
    /// 异步搜索过往对话记忆
    /// </summary>
    Task<IReadOnlyList<MemoryEntry>> SearchPastConversationsAsync(string query, int maxResults = 10, CancellationToken ct = default);

    /// <summary>
    /// 异步构建过往上下文提示文本
    /// </summary>
    Task<PastContextSection> BuildSearchingPastContextSectionAsync(string currentQuery, int maxMemories = 5, CancellationToken ct = default);

    /// <summary>
    /// 异步追加助手日志条目
    /// </summary>
    Task<DailyLogEntry> AppendDailyLogEntryAsync(
        string content,
        DailyLogCategory category = DailyLogCategory.Action,
        string? relatedMemoryId = null,
        CancellationToken ct = default);

    /// <summary>
    /// 异步构建今日日志提示文本
    /// </summary>
    Task<string> BuildDailyLogPromptAsync(int maxEntries = 20, CancellationToken ct = default);

    /// <summary>
    /// 异步同步团队共享记忆
    /// </summary>
    Task<TeamSyncStatus> SyncTeamMemoryAsync(string teamId, CancellationToken ct = default);

    /// <summary>
    /// 获取团队同步状态
    /// </summary>
    TeamSyncStatus? GetTeamSyncStatus(string teamId);

    #endregion
}

/// <summary>
/// 内存健康报告
/// </summary>
public sealed record MemoryHealthReport
{
    /// <summary>
    /// 总记忆数
    /// </summary>
    public int TotalMemories { get; init; }

    /// <summary>
    /// 健康记忆数
    /// </summary>
    public int HealthyMemories { get; init; }

    /// <summary>
    /// 需要关注的记忆数
    /// </summary>
    public int NeedsAttention { get; init; }

    /// <summary>
    /// 建议归档的记忆数
    /// </summary>
    public int ShouldArchive { get; init; }

    /// <summary>
    /// 建议删除的记忆数
    /// </summary>
    public int ShouldDelete { get; init; }

    /// <summary>
    /// 平均健康分数
    /// </summary>
    public double AverageHealthScore { get; init; }

    /// <summary>
    /// 年龄分布
    /// </summary>
    public Dictionary<string, int> AgeDistribution { get; init; } = new();
}

/// <summary>
/// 内存管理服务实现
/// </summary>
[Register]
public sealed partial class MemoryManagementService : IMemoryManagementService, IDisposable
{
    private readonly MemoryStore _memoryStore;
    private readonly List<TeamMemoryPath> _teamMemoryPaths = new();
    private readonly SemaphoreSlim _skillLock;
    [Inject] private readonly ILogger<MemoryManagementService>? _logger;
    private readonly IClockService _clock;
    private readonly MemoryOptionalServices? _optional;

    public MemoryManagementService(
        MemoryStore memoryStore,
        MemoryOptionalServices? optional = null,
        ILogger<MemoryManagementService>? logger = null,
        IClockService? clock = null)
    {
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _skillLock = new SemaphoreSlim(1, 1);
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _optional = optional;
    }

    #region Async Methods Implementation

    /// <inheritdoc />
    public Task<string> AddMemoryAsync(string content, MemoryType type = MemoryType.User, string? title = null, List<string>? tags = null, string? source = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException(L.T(StringKey.VaultContentCannotBeEmptyThrow), nameof(content));

        ct.ThrowIfCancellationRequested();

        _memoryStore.AddMemory(content, type, title, tags, source);

        var entry = _memoryStore.Search(content, type, limit: 1).FirstOrDefault();
        var memoryId = entry?.Id ?? string.Empty;

        _logger?.LogInformation(L.T(StringKey.VaultLogMemoryAdded), memoryId, type);

        return Task.FromResult(memoryId);
    }

    /// <inheritdoc />
    public async Task<MemoryScanResult> ScanMemoriesAsync(string query, string? category = null, int limit = 10, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _skillLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _logger?.LogInformation(L.T(StringKey.VaultLogScanMemory), query, category ?? L.T(StringKey.VaultAllCategory));

            // 将字符串 category 转换为 MemoryType
            MemoryType? memoryType = MemoryTypeExtensions.FromValue(category);

            List<MemoryEntry> results;
            if (_optional?.MemoryScanner != null)
            {
                // 使用 IMemoryScanner 获取记忆
                IReadOnlyList<MemoryEntry> scanResults = memoryType.HasValue
                    ? await _optional.MemoryScanner.ScanByTypeAsync(memoryType.Value, ct).ConfigureAwait(false)
                    : await _optional.MemoryScanner.ScanAllAsync(ct).ConfigureAwait(false);
                results = scanResults.ToList();
            }
            else
            {
                results = _memoryStore.Search(query, memoryType, limit * 2).ToList();
            }

            List<DetailedScoredMemory> scoredMemories;
            if (_optional?.RelevanceSelector != null)
            {
                // 使用 IMemoryRelevanceSelector 进行相关性选择
                var selectedMemories = await _optional.RelevanceSelector.SelectRelevantMemoriesAsync(
                    results, query, limit, ct).ConfigureAwait(false);
                scoredMemories = selectedMemories
                    .Select(sm => new DetailedScoredMemory
                    {
                        Memory = sm.Memory,
                        RelevanceScore = sm.RelevanceScore,
                        MatchReason = GetMatchReason(sm.Memory, query)
                    })
                    .ToList();
            }
            else
            {
                scoredMemories = results.Select(m => new DetailedScoredMemory
                {
                    Memory = m,
                    RelevanceScore = CalculateAdvancedRelevanceScore(m, query),
                    MatchReason = GetMatchReason(m, query)
                })
                .OrderByDescending(m => m.RelevanceScore)
                .Take(limit)
                .ToList();
            }

            // 使用 IMemoryTruncator 对长内容进行截断
            if (_optional?.MemoryTruncator != null)
            {
                scoredMemories = scoredMemories
                    .Select(sm => sm with
                    {
                        Memory = sm.Memory with
                        {
                            Content = _optional.MemoryTruncator.SmartTruncate(sm.Memory.Content, query)
                        }
                    })
                    .ToList();
            }

            var scanResult = new MemoryScanResult
            {
                TotalMemories = results.Count,
                RelevantMemories = scoredMemories,
                ScanTime = _clock.GetUtcNow()
            };

            // 记录搜索历史
            if (_optional?.SearchHistoryService is not null)
            {
                try
                {
                    var topIds = scoredMemories
                        .Take(5)
                        .Select(m => m.Memory.Id)
                        .ToImmutableList();

                    await _optional.SearchHistoryService.RecordSearchAsync(
                        query, scanResult.TotalMemories, topIds, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, L.T(StringKey.VaultLogRecordSearchHistoryFailed), query);
                }
            }

            RecordMemoryMetrics("scan", scanResult.TotalMemories, scanResult.RelevantMemories.Count);

            return scanResult;
        }
        finally
        {
            _skillLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<List<MemoryAgeInfo>> GetMemoryAgeInfoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _skillLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var stats = _memoryStore.GetStatistics();

            var memories = stats.RecentlyAdded
                .Concat(stats.MostAccessed)
                .DistinctBy(m => m.Id);

            if (_optional?.AgeCalculator != null)
            {
                // 使用 IMemoryAgeCalculator 计算老化信息
                return memories.Select(m =>
                {
                    var agedRelevance = _optional?.AgeCalculator.CalculateAgedRelevance(m);
                    var shouldArchive = _optional?.AgeCalculator.ShouldArchive(m);
                    return new MemoryAgeInfo
                    {
                        MemoryId = m.Id,
                        CreatedAt = m.CreatedAt,
                        LastAccessedAt = m.LastAccessedAt,
                        AccessCount = m.AccessCount
                    };
                })
                .ToList();
            }

            return memories.Select(m => new MemoryAgeInfo
            {
                MemoryId = m.Id,
                CreatedAt = m.CreatedAt,
                LastAccessedAt = m.LastAccessedAt,
                AccessCount = m.AccessCount
            })
            .ToList();
        }
        finally
        {
            _skillLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<MemoryHealthReport> GetHealthReportAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var ageInfos = await GetMemoryAgeInfoAsync(ct).ConfigureAwait(false);

        if (ageInfos.Count == 0)
        {
            return new MemoryHealthReport();
        }

        var healthy = ageInfos.Count(a => a.HealthScore >= 50);
        var needsAttention = ageInfos.Count(a => a.HealthScore < 50 && a.HealthScore >= 20);
        var shouldArchive = ageInfos.Count(a => a.ShouldArchive);
        var shouldDelete = ageInfos.Count(a => a.ShouldDelete);

        // 计算年龄分布
        var ageDistribution = new Dictionary<string, int>
        {
            [L.T(StringKey.VaultAgeLess7Days)] = ageInfos.Count(a => a.AgeInDays < 7),
            [L.T(StringKey.VaultAge7To30Days)] = ageInfos.Count(a => a.AgeInDays >= 7 && a.AgeInDays < 30),
            [L.T(StringKey.VaultAge30To90Days)] = ageInfos.Count(a => a.AgeInDays >= 30 && a.AgeInDays < 90),
            [L.T(StringKey.VaultAgeMore90Days)] = ageInfos.Count(a => a.AgeInDays >= 90)
        };

        return new MemoryHealthReport
        {
            TotalMemories = ageInfos.Count,
            HealthyMemories = healthy,
            NeedsAttention = needsAttention,
            ShouldArchive = shouldArchive,
            ShouldDelete = shouldDelete,
            AverageHealthScore = ageInfos.Average(a => a.HealthScore),
            AgeDistribution = ageDistribution
        };
    }

    /// <inheritdoc />
    public async Task AddTeamMemoryPathAsync(string teamId, string path, bool isShared = true, List<string>? allowedAgents = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _skillLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 移除已存在的相同路径
            _teamMemoryPaths.RemoveAll(p => p.TeamId == teamId && p.Path == path);

            _teamMemoryPaths.Add(new TeamMemoryPath
            {
                TeamId = teamId,
                Path = path,
                IsShared = isShared,
                AllowedAgents = allowedAgents ?? new List<string>()
            });

            _logger?.LogInformation(L.T(StringKey.VaultLogAddTeamPath), teamId, path);
        }
        finally
        {
            _skillLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<List<TeamMemoryPath>> GetTeamMemoryPathsAsync(string? teamId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _skillLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return GetTeamMemoryPathsCore(teamId);
        }
        finally
        {
            _skillLock.Release();
        }
    }

    private List<TeamMemoryPath> GetTeamMemoryPathsCore(string? teamId)
    {
        var paths = _teamMemoryPaths.AsEnumerable();

        if (!string.IsNullOrEmpty(teamId))
        {
            paths = paths.Where(p => p.TeamId == teamId);
        }

        return paths.ToList();
    }

    /// <inheritdoc />
    public async Task<bool> RemoveTeamMemoryPathAsync(string teamId, string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _skillLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var removed = _teamMemoryPaths.RemoveAll(p => p.TeamId == teamId && p.Path == path);
            if (removed > 0)
            {
                _logger?.LogInformation(L.T(StringKey.VaultLogRemoveTeamPath), teamId, path);
                return true;
            }
            return false;
        }
        finally
        {
            _skillLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<MemoryScanResult> ScanTeamMemoriesAsync(string teamId, string query, int limit = 10, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _skillLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var teamPaths = GetTeamMemoryPathsCore(teamId);

            if (teamPaths.Count == 0)
            {
                return new MemoryScanResult { TotalMemories = 0 };
            }

            var pathPrefixes = new HashSet<string>(teamPaths.Select(tp => tp.Path), StringComparer.OrdinalIgnoreCase);

            var filteredMemories = _memoryStore.Search(query, null, limit * 2)
                .Where(m => pathPrefixes.Any(prefix => m.Source?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true))
                .ToList();

            List<DetailedScoredMemory> results;
            if (_optional?.RelevanceSelector != null)
            {
                // 使用 IMemoryRelevanceSelector 进行相关性选择
                var selectedMemories = await _optional.RelevanceSelector.SelectRelevantMemoriesAsync(
                    filteredMemories, query, limit, ct).ConfigureAwait(false);
                results = selectedMemories
                    .Select(sm => new DetailedScoredMemory
                    {
                        Memory = sm.Memory,
                        RelevanceScore = sm.RelevanceScore,
                        MatchReason = L.T(StringKey.VaultTeamShared, teamId)
                    })
                    .ToList();
            }
            else
            {
                results = filteredMemories
                    .Select(m => new DetailedScoredMemory
                    {
                        Memory = m,
                        RelevanceScore = CalculateAdvancedRelevanceScore(m, query),
                        MatchReason = L.T(StringKey.VaultTeamShared, teamId)
                    })
                    .OrderByDescending(m => m.RelevanceScore)
                    .Take(limit)
                    .ToList();
            }

            // 使用 IMemoryTruncator 对长内容进行截断
            if (_optional?.MemoryTruncator != null)
            {
                results = results
                    .Select(sm => sm with
                    {
                        Memory = sm.Memory with
                        {
                            Content = _optional.MemoryTruncator.SmartTruncate(sm.Memory.Content, query)
                        }
                    })
                    .ToList();
            }

            return new MemoryScanResult
            {
                TotalMemories = results.Count,
                RelevantMemories = results,
                ScanTime = _clock.GetUtcNow()
            };
        }
        finally
        {
            _skillLock.Release();
        }
    }

    #endregion

    #region Sync Methods Implementation - Obsolete



    /// <inheritdoc />
    public async Task<MemoryCleanupResult> CleanupOldMemoriesAsync(
        int? archiveAfterDays = null,
        int? deleteAfterDays = null,
        CancellationToken ct = default)
    {
        var archiveDays = archiveAfterDays ?? 90;
        var deleteDays = deleteAfterDays ?? 180;

        _logger?.LogInformation(L.T(StringKey.VaultLogStartCleanup),
            archiveDays, deleteDays);

        var ageInfos = await GetMemoryAgeInfoAsync(ct).ConfigureAwait(false);
        var result = new MemoryCleanupResult
        {
            CheckedCount = ageInfos.Count
        };

        foreach (var ageInfo in ageInfos)
        {
            ct.ThrowIfCancellationRequested();

            if (ageInfo.DaysSinceLastAccess > deleteDays && ageInfo.AccessCount < 2)
            {
                // 删除旧且很少访问的记忆
                _memoryStore.DeleteMemory(ageInfo.MemoryId);
                result.DeletedCount++;
                result.ProcessedIds.Add(ageInfo.MemoryId);
                _logger?.LogDebug(L.T(StringKey.VaultLogDeleteMemory), ageInfo.MemoryId);
            }
            else if (_optional?.AgeCalculator != null)
            {
                // 使用 IMemoryAgeCalculator 判断是否应该归档
                var memory = _memoryStore.GetMemory(ageInfo.MemoryId);
                if (memory != null && _optional.AgeCalculator.ShouldArchive(memory))
                {
                    await ArchiveMemoryAsync(ageInfo.MemoryId, ct).ConfigureAwait(false);
                    result.ArchivedCount++;
                    result.ProcessedIds.Add(ageInfo.MemoryId);
                    _logger?.LogDebug(L.T(StringKey.VaultLogArchiveMemory), ageInfo.MemoryId);
                }
                else
                {
                    result.RetainedCount++;
                }
            }
            else if (ageInfo.DaysSinceLastAccess > archiveDays && ageInfo.AccessCount < 3)
            {
                // 归档较旧且访问较少的记忆
                await ArchiveMemoryAsync(ageInfo.MemoryId, ct).ConfigureAwait(false);
                result.ArchivedCount++;
                result.ProcessedIds.Add(ageInfo.MemoryId);
                _logger?.LogDebug(L.T(StringKey.VaultLogArchiveMemory), ageInfo.MemoryId);
            }
            else
            {
                result.RetainedCount++;
            }
        }

        _logger?.LogInformation(L.T(StringKey.VaultLogCleanupComplete),
            result.CheckedCount, result.ArchivedCount, result.DeletedCount, result.RetainedCount);

        RecordMemoryMetrics("cleanup", result.CheckedCount, result.ArchivedCount + result.DeletedCount);

        return result;
    }

    /// <inheritdoc />
    public Task<bool> ArchiveMemoryAsync(string memoryId, CancellationToken ct = default)
    {
        _logger?.LogWarning(L.T(StringKey.VaultLogArchiveNotImplemented), memoryId);
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> RestoreMemoryAsync(string memoryId, CancellationToken ct = default)
    {
        _logger?.LogWarning(L.T(StringKey.VaultLogRestoreNotImplemented), memoryId);
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryEntry>> SearchPastConversationsAsync(
        string query, int maxResults = 10, CancellationToken ct = default)
    {
        if (_optional?.SearchHistoryService is null)
        {
            _logger?.LogWarning(L.T(StringKey.VaultLogSearchHistoryNotRegistered));
            return Task.FromResult<IReadOnlyList<MemoryEntry>>(Array.Empty<MemoryEntry>());
        }

        return _optional.SearchHistoryService.SearchPastConversationsAsync(query, maxResults, ct);
    }

    /// <inheritdoc />
    public Task<PastContextSection> BuildSearchingPastContextSectionAsync(
        string currentQuery, int maxMemories = 5, CancellationToken ct = default)
    {
        if (_optional?.SearchHistoryService is null)
        {
            _logger?.LogWarning(L.T(StringKey.VaultLogSearchHistoryNotRegisteredContext));
            return Task.FromResult(new PastContextSection
            {
                PromptText = string.Empty,
                ReferencedMemoryCount = 0
            });
        }

        return _optional.SearchHistoryService.BuildSearchingPastContextSectionAsync(currentQuery, maxMemories, ct);
    }

    /// <inheritdoc />
    public Task<DailyLogEntry> AppendDailyLogEntryAsync(
        string content,
        DailyLogCategory category = DailyLogCategory.Action,
        string? relatedMemoryId = null,
        CancellationToken ct = default)
    {
        if (_optional?.DailyLogService is null)
        {
            _logger?.LogWarning(L.T(StringKey.VaultLogDailyLogNotRegistered));
            return Task.FromResult(new DailyLogEntry
            {
                Content = content,
                Category = category,
                RelatedMemoryId = relatedMemoryId
            });
        }

        return _optional.DailyLogService.AppendEntryAsync(content, category, relatedMemoryId, ct);
    }

    /// <inheritdoc />
    public Task<string> BuildDailyLogPromptAsync(int maxEntries = 20, CancellationToken ct = default)
    {
        if (_optional?.DailyLogService is null)
        {
            _logger?.LogWarning(L.T(StringKey.VaultLogDailyLogNotRegisteredPrompt));
            return Task.FromResult(string.Empty);
        }

        return _optional.DailyLogService.BuildDailyLogPromptAsync(maxEntries, ct);
    }

    /// <inheritdoc />
    public Task<TeamSyncStatus> SyncTeamMemoryAsync(string teamId, CancellationToken ct = default)
    {
        if (_optional?.TeamMemorySyncService is null)
        {
            _logger?.LogWarning(L.T(StringKey.VaultLogTeamSyncNotRegistered));
            return Task.FromResult(new TeamSyncStatus
            {
                TeamId = teamId,
                IsWatching = false,
                SyncedMemoryCount = 0
            });
        }

        return _optional.TeamMemorySyncService.SyncTeamMemoryAsync(teamId, ct);
    }

    /// <inheritdoc />
    public TeamSyncStatus? GetTeamSyncStatus(string teamId)
    {
        return _optional?.TeamMemorySyncService?.GetSyncStatus(teamId);
    }

    #endregion

    #region Private Methods

    private double CalculateAdvancedRelevanceScore(MemoryEntry memory, string query)
    {
        var score = 0.0;
        var queryWords = QueryWordHelper.ExtractQueryWords(query);
        var contentSpan = memory.Content.AsSpan();

        for (var i = 0; i < queryWords.Length; i++)
        {
            var wordSpan = queryWords[i].AsSpan();
            if (QueryWordHelper.ContainsOrdinalIgnoreCase(contentSpan, wordSpan))
            {
                score += 1.0;

                if (QueryWordHelper.ContainsWholeWordOrdinalIgnoreCase(contentSpan, wordSpan))
                {
                    score += 0.5;
                }
            }
        }

        // 标签匹配（权重更高）
        foreach (var tag in memory.Tags)
        {
            if (queryWords.Any(w => tag.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2.0;
            }
        }

        // 类型匹配
        if (queryWords.Any(w => memory.Type.ToString().Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            score += 1.5;
        }

        // 访问频率加权
        score *= (1 + Math.Log(1 + memory.AccessCount));

        // 时间衰减（越新的记忆分数越高）
        var daysSinceCreated = (_clock.GetUtcNow() - memory.CreatedAt).TotalDays;
        score *= Math.Exp(-daysSinceCreated / 30.0);

        return score;
    }

    private string? GetMatchReason(MemoryEntry memory, string query)
    {
        var reasons = new List<string>();
        var queryWords = QueryWordHelper.ExtractQueryWords(query);

        if (queryWords.Any(w => memory.Content.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add(L.T(StringKey.VaultMatchReasonContent));
        }

        // 检查标签匹配
        if (memory.Tags.Any(t => queryWords.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase))))
        {
            reasons.Add(L.T(StringKey.VaultMatchReasonTag));
        }

        // 检查类型匹配
        if (queryWords.Any(w => memory.Type.ToString().Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add(L.T(StringKey.VaultMatchReasonType));
        }

        return reasons.Count > 0 ? string.Join(", ", reasons) : null;
    }

    private void RecordMemoryMetrics(string operation, int totalCount, int relevantCount)
    {
        _optional?.TelemetryService?.RecordCount("memory.operation.count", new Dictionary<string, string> { ["operation"] = operation }, "count", "Memory operation count");
        _optional?.TelemetryService?.RecordHistogram("memory.operation.total", totalCount, new Dictionary<string, string> { ["operation"] = operation }, "count", "Memory operation total items");
    }

    #endregion

    public void Dispose() => _skillLock.Dispose();
}
