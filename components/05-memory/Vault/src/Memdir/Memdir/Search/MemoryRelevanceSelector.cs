
namespace Core.Memdir;

/// <summary>
/// AI 相关性选择器接口
/// 基于查询语义选择最相关的记忆
/// </summary>
public interface IMemoryRelevanceSelector
{
    /// <summary>
    /// 选择最相关的记忆
    /// </summary>
    /// <param name="memories">所有记忆</param>
    /// <param name="query">查询内容</param>
    /// <param name="maxResults">最大结果数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>相关记忆列表</returns>
    Task<IReadOnlyList<ScoredMemory>> SelectRelevantMemoriesAsync(
        IEnumerable<MemoryEntry> memories,
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 带分数的记忆
/// </summary>
public sealed record ScoredMemory(MemoryEntry Memory, double RelevanceScore);

/// <summary>
/// AI 相关性选择器实现
/// 使用关键词匹配和语义相似度计算
/// </summary>
[Register]
public sealed partial class MemoryRelevanceSelector : IMemoryRelevanceSelector
{
    private readonly IMemoryAgeCalculator _ageCalculator;
    [Inject] private readonly ILogger<MemoryRelevanceSelector>? _logger;
    private readonly IClockService _clock;

    public MemoryRelevanceSelector(
        IMemoryAgeCalculator ageCalculator,
        ILogger<MemoryRelevanceSelector>? logger = null,
        IClockService? clock = null)
    {
        _ageCalculator = ageCalculator ?? throw new ArgumentNullException(nameof(ageCalculator));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScoredMemory>> SelectRelevantMemoriesAsync(
        IEnumerable<MemoryEntry> memories,
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var queryWords = QueryWordHelper.ExtractWords(query, minLength: 2);

        var scoredMemories = memories
            .Where(m => !m.IsArchived && !m.IsExpired(now))
            .Select(m => ScoreMemory(m, queryWords, now))
            .Where(sm => sm.RelevanceScore > 0)
            .OrderByDescending(sm => sm.RelevanceScore)
            .Take(maxResults)
            .ToImmutableList();

        _logger?.LogDebug(
            "Selected {Count} relevant memories from {Total} for query: {Query}",
            scoredMemories.Count,
            memories.Count(),
            query[..Math.Min(50, query.Length)]);

        return Task.FromResult<IReadOnlyList<ScoredMemory>>(scoredMemories);
    }

    /// <summary>
    /// 为单个记忆打分
    /// </summary>
    private ScoredMemory ScoreMemory(MemoryEntry memory, HashSet<string> queryWords, DateTime now)
    {
        var score = 0.0;

        // 1. 关键词匹配分数
        var memoryWords = QueryWordHelper.ExtractWords(memory.Content);
        var matchingWords = queryWords.Intersect(memoryWords, StringComparer.OrdinalIgnoreCase).Count();
        var keywordScore = matchingWords > 0
            ? (double)matchingWords / queryWords.Count * 0.4
            : 0;
        score += keywordScore;

        // 2. 标签匹配分数
        var tagMatches = memory.Tags
            .Count(tag => queryWords.Any(qw => tag.Contains(qw, StringComparison.OrdinalIgnoreCase)));
        score += tagMatches * 0.15;

        // 3. 标题匹配分数
        if (!string.IsNullOrEmpty(memory.Title))
        {
            var titleWords = QueryWordHelper.ExtractWords(memory.Title);
            var titleMatches = queryWords.Intersect(titleWords, StringComparer.OrdinalIgnoreCase).Count();
            score += (double)titleMatches / queryWords.Count * 0.2;
        }

        // 4. 类型权重
        score *= memory.Type.GetBaseRelevanceWeight();

        // 5. 应用老化计算
        var agedScore = _ageCalculator.CalculateAgedRelevance(memory, now);
        score = score * 0.5 + agedScore * 0.5;

        // 6. 访问频率加权
        score *= (1 + Math.Log(1 + memory.AccessCount) * 0.1);

        return new ScoredMemory(memory, Math.Min(score, 1.0));
    }

}
