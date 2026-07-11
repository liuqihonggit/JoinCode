
namespace Core.Memdir;

/// <summary>
/// 内存存储 - 持久化记忆管理
/// </summary>
[Register]
public sealed partial class MemoryStore : IDisposable
{
    private readonly ConcurrentDictionary<string, MemoryEntry> _memories;
    private readonly string _storagePath;
    [Inject] private readonly ILogger<MemoryStore>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly IFileOperationService _fileOperationService;
    private readonly CancellationTokenSource _disposeCts = new();

    public MemoryStore(IOptions<MemdirOptions> options, IFileOperationService fileOperationService, ILogger<MemoryStore>? logger = null, IClockService? clock = null)
    {
        _storagePath = options?.Value?.StoragePath ?? throw new ArgumentNullException(nameof(options));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _memories = new ConcurrentDictionary<string, MemoryEntry>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 异步初始化 - 必须在构造函数后调用
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadMemoriesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加记忆
    /// </summary>
    public void AddMemory(string content, MemoryType type = MemoryType.User, string? title = null, List<string>? tags = null, string? source = null)
    {
        var entry = MemoryEntry.Create(
            type: type,
            content: content,
            title: title,
            tags: tags,
            source: source,
            now: _clock.GetUtcNow());

        _memories[entry.Id] = entry;
        _logger?.LogInformation(L.T(StringKey.VaultLogStoreAddMemory), entry.Id, type);

        _ = SaveMemoriesAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 搜索记忆
    /// </summary>
    public IEnumerable<MemoryEntry> Search(string query, MemoryType? type = null, int limit = 10)
    {
        IEnumerable<MemoryEntry> results = _memories.Values;

        // 按类型过滤
        if (type.HasValue)
        {
            results = results.Where(m => m.Type == type.Value);
        }

        // 计算相关性分数
        var scoredResults = results.Select(m => new
        {
            Memory = m,
            Score = CalculateRelevanceScore(m, query)
        })
        .Where(x => x.Score > 0)
        .OrderByDescending(x => x.Score)
        .Take(limit)
        .ToList();

        // 更新访问统计
        foreach (var result in scoredResults)
        {
            var updated = result.Memory.WithAccessed(_clock.GetUtcNow());
            _memories[updated.Id] = updated;
        }

        return scoredResults.Select(x => x.Memory);
    }

    /// <summary>
    /// 按标签搜索 - 使用 HashSet 优化 O(n²) 查找
    /// </summary>
    public IEnumerable<MemoryEntry> SearchByTags(List<string> tags, int limit = 10)
    {
        // 使用 HashSet 缓存标签，实现 O(1) 查找
        var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);

        return _memories.Values
            .Where(m => m.Tags.Any(t => tagSet.Contains(t)))
            .OrderByDescending(m => m.AccessCount)
            .ThenByDescending(m => m.CreatedAt)
            .Take(limit);
    }

    /// <summary>
    /// 按类型搜索
    /// </summary>
    public IEnumerable<MemoryEntry> SearchByType(MemoryType type, int limit = 10)
    {
        return _memories.Values
            .Where(m => m.Type == type)
            .OrderByDescending(m => m.AccessCount)
            .ThenByDescending(m => m.CreatedAt)
            .Take(limit);
    }

    /// <summary>
    /// 获取记忆
    /// </summary>
    public MemoryEntry? GetMemory(string id)
    {
        if (_memories.TryGetValue(id, out var memory))
        {
            var updated = memory.WithAccessed(_clock.GetUtcNow());
            _memories[updated.Id] = updated;
            return updated;
        }

        return null;
    }

    /// <summary>
    /// 删除记忆
    /// </summary>
    public bool DeleteMemory(string id)
    {
        if (!_memories.TryRemove(id, out _))
        {
            return false;
        }

        _logger?.LogInformation(L.T(StringKey.VaultLogStoreDeleteMemory), id);
        _ = SaveMemoriesAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// 归档记忆
    /// </summary>
    public bool ArchiveMemory(string id)
    {
        if (!_memories.TryGetValue(id, out var memory))
        {
            return false;
        }

        var archived = memory.WithArchived(_clock.GetUtcNow());
        _memories[id] = archived;

        _logger?.LogInformation(L.T(StringKey.VaultLogStoreArchiveMemory), id);
        _ = SaveMemoriesAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// 获取所有记忆类型
    /// </summary>
    public IEnumerable<MemoryType> GetTypes()
    {
        return _memories.Values.Select(m => m.Type).Distinct().OrderBy(t => t);
    }

    /// <summary>
    /// 获取所有标签
    /// </summary>
    public IEnumerable<string> GetAllTags()
    {
        return _memories.Values.SelectMany(m => m.Tags).Distinct().OrderBy(t => t);
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public MemoryStatistics GetStatistics()
    {
        var memories = _memories.Values.ToList();

        return new MemoryStatistics
        {
            TotalCount = memories.Count,
            TypeCounts = memories.GroupBy(m => m.Type)
                .ToDictionary(g => g.Key, g => g.Count()),
            TagCounts = memories.SelectMany(m => m.Tags)
                .GroupBy(t => t)
                .ToDictionary(g => g.Key, g => g.Count()),
            MostAccessed = memories.OrderByDescending(m => m.AccessCount).Take(5).ToList(),
            RecentlyAdded = memories.OrderByDescending(m => m.CreatedAt).Take(5).ToList(),
            ArchivedCount = memories.Count(m => m.IsArchived),
            ExpiredCount = memories.Count(m => m.IsExpired())
        };
    }

    /// <summary>
    /// 清理过期记忆
    /// </summary>
    public int CleanupExpired()
    {
        var expiredIds = _memories.Values
            .Where(m => m.IsExpired() && !m.IsArchived)
            .Select(m => m.Id)
            .ToArray();

        foreach (var id in expiredIds)
        {
            _memories.TryRemove(id, out _);
        }

        if (expiredIds.Length > 0)
        {
            _logger?.LogInformation(L.T(StringKey.VaultLogStoreCleanedExpired), expiredIds.Length);
            _ = SaveMemoriesAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        }

        return expiredIds.Length;
    }

    /// <summary>
    /// 自动提取记忆（简化实现）
    /// </summary>
    public void AutoExtractMemory(string content, string source)
    {
        // 提取关键信息（简化实现）
        var patterns = new Dictionary<string, MemoryType>
        {
            [@"重要[:：]\s*(.+?)(?=\n|$)"] = MemoryType.User,
            [@"记住[:：]\s*(.+?)(?=\n|$)"] = MemoryType.User,
            [@"TODO[:：]\s*(.+?)(?=\n|$)"] = MemoryType.Project,
            [@"FIXME[:：]\s*(.+?)(?=\n|$)"] = MemoryType.Project,
            [@"决策[:：]\s*(.+?)(?=\n|$)"] = MemoryType.Feedback
        };

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(content, pattern.Key, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var memoryContent = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(memoryContent) && memoryContent.Length > 10)
                    {
                        AddMemory(memoryContent, pattern.Value, null, new List<string> { "auto" }, source);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 计算相关性分数
    /// </summary>
    private double CalculateRelevanceScore(MemoryEntry memory, string query)
    {
        var score = 0.0;
        var queryWords = QueryWordHelper.ExtractQueryWords(query);

        var contentSpan = memory.Content.AsSpan();
        for (var i = 0; i < queryWords.Length; i++)
        {
            if (QueryWordHelper.ContainsOrdinalIgnoreCase(contentSpan, queryWords[i].AsSpan()))
            {
                score += 1.0;
            }
        }

        if (!string.IsNullOrEmpty(memory.Title))
        {
            var titleSpan = memory.Title.AsSpan();
            for (var i = 0; i < queryWords.Length; i++)
            {
                if (QueryWordHelper.ContainsOrdinalIgnoreCase(titleSpan, queryWords[i].AsSpan()))
                {
                    score += 2.0;
                }
            }
        }

        // 标签匹配
        foreach (var tag in memory.Tags)
        {
            if (queryWords.Any(w => tag.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2.0;
            }
        }

        // 访问频率加权
        score *= (1 + Math.Log(1 + memory.AccessCount));

        // 时间衰减
        var daysSinceCreated = (_clock.GetUtcNow() - memory.CreatedAt).TotalDays;
        score *= Math.Exp(-daysSinceCreated / 30.0); // 30天衰减

        // 类型权重
        score *= memory.Type.GetBaseRelevanceWeight();

        return score;
    }

    /// <summary>
    /// 加载记忆
    /// </summary>
    private async Task LoadMemoriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _fileOperationService.ReadFileAsync(_storagePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                return;
            }

            var memories = JsonSerializer.Deserialize(result.Content, MemdirJsonContext.Default.ListMemoryEntry);

            if (memories != null)
            {
                foreach (var memory in memories)
                {
                    _memories[memory.Id] = memory;
                }

                _logger?.LogInformation(L.T(StringKey.VaultLogStoreLoadedMemories), memories.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogStoreLoadFailed));
        }
    }

    /// <summary>
    /// 保存记忆
    /// </summary>
    private async Task SaveMemoriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var memories = _memories.Values.ToList();
            var json = JsonSerializer.Serialize(memories, MemdirIndentedJsonContext.Default.ListMemoryEntry);

            var result = await _fileOperationService.WriteFileAsync(_storagePath, json, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                _logger?.LogDebug(L.T(StringKey.VaultLogStoreSavedMemories), memories.Count);
            }
            else
            {
                _logger?.LogError(L.T(StringKey.VaultLogStoreSaveFailedError), result.ErrorMessage);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogStoreSaveFailed));
        }
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}

/// <summary>
/// 内存统计
/// </summary>
public sealed partial class MemoryStatistics
{
    public int TotalCount { get; init; }
    public Dictionary<MemoryType, int> TypeCounts { get; init; } = new();
    public Dictionary<string, int> TagCounts { get; init; } = new();
    public IReadOnlyList<MemoryEntry> MostAccessed { get; init; } = Array.Empty<MemoryEntry>();
    public IReadOnlyList<MemoryEntry> RecentlyAdded { get; init; } = Array.Empty<MemoryEntry>();
    public int ArchivedCount { get; init; }
    public int ExpiredCount { get; init; }
}
