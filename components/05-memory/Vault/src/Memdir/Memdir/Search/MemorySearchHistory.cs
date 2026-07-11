
namespace Core.Memdir;

/// <summary>
/// 搜索历史条目模型
/// 记录一次记忆搜索的查询与结果摘要
/// </summary>
public sealed record SearchHistoryEntry
{
    /// <summary>
    /// 搜索查询内容
    /// </summary>
    [JsonPropertyName("query")]
    public required string Query { get; init; }

    /// <summary>
    /// 搜索结果数量
    /// </summary>
    [JsonPropertyName("resultCount")]
    public int ResultCount { get; init; }

    /// <summary>
    /// 搜索结果中排名靠前的记忆 ID
    /// </summary>
    [JsonPropertyName("topMemoryIds")]
    public ImmutableList<string> TopMemoryIds { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// 搜索时间
    /// </summary>
    [JsonPropertyName("searchedAt")]
    public DateTime SearchedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 过往上下文片段模型
/// 描述从历史对话记忆中检索到的相关上下文
/// </summary>
public sealed record PastContextSection
{
    /// <summary>
    /// 构建的提示文本
    /// </summary>
    [JsonPropertyName("promptText")]
    public required string PromptText { get; init; }

    /// <summary>
    /// 引用的记忆条目数量
    /// </summary>
    [JsonPropertyName("referencedMemoryCount")]
    public int ReferencedMemoryCount { get; init; }

    /// <summary>
    /// 引用的记忆 ID 列表
    /// </summary>
    [JsonPropertyName("referencedMemoryIds")]
    public ImmutableList<string> ReferencedMemoryIds { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// 构建时间
    /// </summary>
    [JsonPropertyName("builtAt")]
    public DateTime BuiltAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 记忆搜索历史服务接口
/// 管理搜索历史记录，并支持从历史对话记忆中检索相关上下文
/// </summary>
public interface IMemorySearchHistoryService : IDisposable
{
    /// <summary>
    /// 记录一次搜索
    /// </summary>
    /// <param name="query">搜索查询</param>
    /// <param name="resultCount">结果数量</param>
    /// <param name="topMemoryIds">排名靠前的记忆 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记录的搜索历史条目</returns>
    Task<SearchHistoryEntry> RecordSearchAsync(
        string query,
        int resultCount,
        ImmutableList<string>? topMemoryIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 搜索过往对话记忆
    /// </summary>
    /// <param name="query">搜索查询</param>
    /// <param name="maxResults">最大结果数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的记忆条目列表</returns>
    Task<IReadOnlyList<MemoryEntry>> SearchPastConversationsAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 构建过往上下文提示文本
    /// </summary>
    /// <param name="currentQuery">当前查询</param>
    /// <param name="maxMemories">最大引用记忆数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>过往上下文片段</returns>
    Task<PastContextSection> BuildSearchingPastContextSectionAsync(
        string currentQuery,
        int maxMemories = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取最近的搜索历史
    /// </summary>
    /// <param name="limit">最大条目数</param>
    /// <returns>搜索历史条目列表</returns>
    IReadOnlyList<SearchHistoryEntry> GetRecentSearches(int limit = 20);
}

/// <summary>
/// 记忆搜索历史服务实现
/// 维护搜索历史队列，并基于历史查询从 MemoryStore 中
/// 检索相关的过往对话记忆，构建上下文提示
/// </summary>
[Register]
public sealed partial class MemorySearchHistoryService : IMemorySearchHistoryService, IDisposable
{
    private const int MaxHistorySize = 100;

    private readonly MemoryStore _memoryStore;
    [Inject] private readonly ILogger<MemorySearchHistoryService>? _logger;
    private readonly IClockService _clock;
    private readonly SemaphoreSlim _historyLock;

    /// <summary>
    /// 搜索历史记录（按时间倒序）
    /// </summary>
    private readonly ConcurrentDeque<SearchHistoryEntry> _searchHistory;

    public MemorySearchHistoryService(
        MemoryStore memoryStore,
        ILogger<MemorySearchHistoryService>? logger = null,
        IClockService? clock = null)
    {
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _historyLock = new SemaphoreSlim(1, 1);
        _searchHistory = new ConcurrentDeque<SearchHistoryEntry>();
    }

    /// <inheritdoc />
    public async Task<SearchHistoryEntry> RecordSearchAsync(
        string query,
        int resultCount,
        ImmutableList<string>? topMemoryIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        cancellationToken.ThrowIfCancellationRequested();
        await _historyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = new SearchHistoryEntry
            {
                Query = query,
                ResultCount = resultCount,
                TopMemoryIds = topMemoryIds ?? ImmutableList<string>.Empty,
                SearchedAt = _clock.GetUtcNow()
            };

            _searchHistory.Prepend(entry);

            // 超出容量时移除最旧的记录
            while (_searchHistory.Count > MaxHistorySize)
            {
                _searchHistory.TryTakeBack(out _);
            }

            _logger?.LogDebug(
                L.T(StringKey.VaultLogRecordSearch),
                query[..Math.Min(50, query.Length)],
                resultCount);

            return entry;
        }
        finally
        {
            _historyLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryEntry>> SearchPastConversationsAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        cancellationToken.ThrowIfCancellationRequested();

        // 从 MemoryStore 搜索，优先匹配 Feedback 和 User 类型（过往对话记忆）
        var feedbackResults = _memoryStore.Search(query, MemoryType.Feedback, maxResults);
        var userResults = _memoryStore.Search(query, MemoryType.User, maxResults);

        // 合并去重，按相关性排序
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var combinedResults = new List<MemoryEntry>();

        foreach (var memory in feedbackResults.Concat(userResults))
        {
            if (seenIds.Add(memory.Id))
            {
                combinedResults.Add(memory);
            }
        }

        var result = combinedResults
            .OrderByDescending(m => m.RelevanceScore)
            .ThenByDescending(m => m.CreatedAt)
            .Take(maxResults)
            .ToImmutableList();

        _logger?.LogDebug(
            L.T(StringKey.VaultLogSearchPastConversations),
            query[..Math.Min(50, query.Length)],
            result.Count);

        return Task.FromResult<IReadOnlyList<MemoryEntry>>(result);
    }

    /// <inheritdoc />
    public async Task<PastContextSection> BuildSearchingPastContextSectionAsync(
        string currentQuery,
        int maxMemories = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentQuery);

        cancellationToken.ThrowIfCancellationRequested();

        // 1. 从过往对话中搜索相关记忆
        var pastMemories = await SearchPastConversationsAsync(
            currentQuery, maxMemories, cancellationToken).ConfigureAwait(false);

        if (pastMemories.Count == 0)
        {
            return new PastContextSection
            {
                PromptText = string.Empty,
                ReferencedMemoryCount = 0
            };
        }

        // 2. 构建提示文本
        var sb = new StringBuilder();
        sb.AppendLine(L.T(StringKey.VaultPastContextHeader));
        sb.AppendLine();
        sb.AppendLine(L.T(StringKey.VaultPastContextIntro));
        sb.AppendLine();

        var referencedIds = new List<string>();

        for (var i = 0; i < pastMemories.Count; i++)
        {
            var memory = pastMemories[i];
            referencedIds.Add(memory.Id);

            var typeLabel = memory.Type.GetName();
            var ageDays = (_clock.GetUtcNow() - memory.CreatedAt).Days;
            var ageLabel = ageDays switch
            {
                0 => L.T(StringKey.VaultTodayTime),
                1 => L.T(StringKey.VaultYesterdayTime),
                < 7 => L.T(StringKey.VaultDaysAgoTime, ageDays),
                < 30 => L.T(StringKey.VaultWeeksAgoTime, ageDays / 7),
                _ => L.T(StringKey.VaultMonthsAgoTime, ageDays / 30)
            };

            sb.AppendLine($"### {i + 1}. [{typeLabel}] {memory.Title ?? L.T(StringKey.VaultNoTitleDefault)} ({ageLabel})");
            sb.AppendLine(memory.Content);

            if (!memory.Tags.IsEmpty)
            {
                sb.AppendLine(L.T(StringKey.VaultLabelTagsInline, string.Join(", ", memory.Tags)));
            }

            sb.AppendLine();
        }

        // 3. 补充相关搜索历史
        var relatedSearches = GetRecentSearches(5)
            .Where(s => IsQueryRelated(s.Query, currentQuery))
            .Take(3)
            .ToList();

        if (relatedSearches.Count > 0)
        {
            sb.AppendLine(L.T(StringKey.VaultRelatedSearchHeader));
            foreach (var search in relatedSearches)
            {
                var timeStr = search.SearchedAt.ToString("yyyy-MM-dd HH:mm");
                sb.AppendLine(L.T(StringKey.VaultRelatedSearchResult, timeStr, search.Query, search.ResultCount));
            }
            sb.AppendLine();
        }

        var section = new PastContextSection
        {
            PromptText = sb.ToString(),
            ReferencedMemoryCount = pastMemories.Count,
            ReferencedMemoryIds = referencedIds.ToImmutableList()
        };

        _logger?.LogDebug(
            L.T(StringKey.VaultLogBuildPastContext),
            currentQuery[..Math.Min(30, currentQuery.Length)],
            pastMemories.Count);

        return section;
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchHistoryEntry> GetRecentSearches(int limit = 20)
    {
        return _searchHistory.Take(limit).ToImmutableList();
    }

    /// <summary>
    /// 判断两个查询是否相关（基于关键词重叠度）
    /// </summary>
    private static bool IsQueryRelated(string query1, string query2)
    {
        var words1 = QueryWordHelper.ExtractWords(query1, minLength: 2);
        var words2 = QueryWordHelper.ExtractWords(query2, minLength: 2);

        if (words1.Count == 0 || words2.Count == 0)
        {
            return false;
        }

        var overlap = words1.Intersect(words2, StringComparer.OrdinalIgnoreCase).Count();
        var minCount = Math.Min(words1.Count, words2.Count);

        // 重叠度超过 30% 视为相关
        return (double)overlap / minCount > 0.3;
    }

    public void Dispose() => _historyLock.Dispose();
}

/// <summary>
/// 简易双端队列（线程安全）
/// 用于搜索历史的 FIFO 管理
/// </summary>
internal sealed class ConcurrentDeque<T>
{
    private readonly LinkedList<T> _list;
    private readonly object _lockObj;

    public ConcurrentDeque()
    {
        _list = new LinkedList<T>();
        _lockObj = new object();
    }

    public int Count
    {
        get
        {
            lock (_lockObj)
            {
                return _list.Count;
            }
        }
    }

    public void Prepend(T item)
    {
        lock (_lockObj)
        {
            _list.AddFirst(item);
        }
    }

    public bool TryTakeBack(out T? item)
    {
        lock (_lockObj)
        {
            if (_list.Count == 0)
            {
                item = default;
                return false;
            }

            item = _list.Last!.Value;
            _list.RemoveLast();
            return true;
        }
    }

    public IReadOnlyList<T> Take(int count)
    {
        lock (_lockObj)
        {
            return _list.Take(count).ToImmutableList();
        }
    }
}
