
namespace Core.Skills.Search;

/// <summary>
/// 技能搜索服务 — 基于关键词、标签、类别和上下文进行技能检索和推荐
/// </summary>
[Register(typeof(ISkillSearchService), ServiceLifetime.Singleton)]
[Register(typeof(JoinCode.Abstractions.Interfaces.ISkillSearchService), ServiceLifetime.Singleton)]
public sealed partial class SkillSearchService : ServiceEntity, ISkillSearchService, JoinCode.Abstractions.Interfaces.ISkillSearchService {
    private readonly ISkillService _skillService;
    private readonly ILogger<SkillSearchService>? _logger;
    private readonly ConcurrentDictionary<string, FrozenSet<string>> _tagIndex = new();
    private readonly ConcurrentDictionary<string, string> _nameIndex = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastIndexTime = DateTime.MinValue;
    private readonly AsyncLock _indexLock = new();

    /// <summary>
    /// 创建技能搜索服务
    /// </summary>
    /// <param name="skillService">技能服务</param>
    /// <param name="logger">日志记录器</param>
    public SkillSearchService(
        ISkillService skillService,
        ILogger<SkillSearchService>? logger = null) {
        ArgumentNullException.ThrowIfNull(skillService);
        _skillService = skillService;
        _logger = logger;
    }

    /// <summary>
    /// 异步搜索技能 — 按关键词、标签、类别匹配并分页返回
    /// </summary>
    /// <param name="query">搜索查询</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>搜索结果列表</returns>
    public async Task<IReadOnlyList<SkillSearchResult>> SearchAsync(
        SkillSearchQuery query,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(query);

        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);

        var skills = await _skillService.GetAvailableSkillsAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SkillSearchResult>();

        foreach (var skill in skills) {
            var score = CalculateRelevanceScore(skill, query);
            if (score > 0) {
                var (matchType, matchedField) = DetermineMatchType(skill, query);
                results.Add(new SkillSearchResult {
                    SkillName = skill.Name,
                    Description = skill.Description,
                    RelevanceScore = score,
                    MatchType = matchType,
                    Tags = skill.Tags,
                    Category = skill.Namespace,
                    MatchedField = matchedField,
                    Highlight = GenerateHighlight(skill, query)
                });
            }
        }

        var sorted = results
            .OrderByDescending(r => r.RelevanceScore)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        _logger?.LogDebug(L.T(StringKey.SkillSearchIndexRebuilt), query.Keyword, sorted.Count);

        return sorted;
    }

    /// <summary>
    /// 基于上下文异步推荐技能 — 提取上下文关键词后按相关性排序
    /// </summary>
    /// <param name="context">上下文描述</param>
    /// <param name="maxResults">最大返回结果数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>推荐结果列表</returns>
    public async Task<IReadOnlyList<SkillSearchResult>> RecommendAsync(
        string context,
        int maxResults = 5,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(context);

        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);

        var skills = await _skillService.GetAvailableSkillsAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SkillSearchResult>();

        var contextKeywords = ExtractKeywords(context);

        foreach (var skill in skills) {
            var score = CalculateContextRelevance(skill, contextKeywords);
            if (score > 0.2) {
                results.Add(new SkillSearchResult {
                    SkillName = skill.Name,
                    Description = skill.Description,
                    RelevanceScore = score,
                    MatchType = SkillMatchType.ContextRecommendation,
                    Tags = skill.Tags,
                    Category = skill.Namespace
                });
            }
        }

        return results
            .OrderByDescending(r => r.RelevanceScore)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// 流式搜索技能 — 逐项产出匹配的技能结果
    /// </summary>
    /// <param name="query">搜索查询</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>搜索结果异步枚举</returns>
    public async IAsyncEnumerable<SkillSearchResult> SearchStreamAsync(
        SkillSearchQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(query);

        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);

        var skills = await _skillService.GetAvailableSkillsAsync(cancellationToken).ConfigureAwait(false);

        foreach (var skill in skills) {
            cancellationToken.ThrowIfCancellationRequested();

            var score = CalculateRelevanceScore(skill, query);
            if (score > 0) {
                var (matchType, matchedField) = DetermineMatchType(skill, query);
                yield return new SkillSearchResult {
                    SkillName = skill.Name,
                    Description = skill.Description,
                    RelevanceScore = score,
                    MatchType = matchType,
                    Tags = skill.Tags,
                    Category = skill.Namespace,
                    MatchedField = matchedField,
                    Highlight = GenerateHighlight(skill, query)
                };
            }
        }
    }

    private async Task EnsureIndexAsync(CancellationToken cancellationToken) {
        if ((DateTime.UtcNow - _lastIndexTime).TotalMinutes < 5) return;

        using var guard = await _indexLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_indexLock.Name}' 等待超时");

        if ((DateTime.UtcNow - _lastIndexTime).TotalMinutes < 5) return;

        var skills = await _skillService.GetAvailableSkillsAsync(cancellationToken).ConfigureAwait(false);
        _tagIndex.Clear();
        _nameIndex.Clear();

        foreach (var skill in skills) {
            _nameIndex[skill.Name] = skill.Name;
            if (skill.Tags.Count > 0) {
                _tagIndex[skill.Name] = skill.Tags
                    .Select(t => t.ToLowerInvariant())
                    .ToFrozenSet();
            }
        }

        _lastIndexTime = DateTime.UtcNow;
        _logger?.LogDebug(L.T(StringKey.SkillSearchIndexRebuilt), skills.Count);

    }

    private static double CalculateRelevanceScore(SkillDefinition skill, SkillSearchQuery query) {
        var score = 0.0;

        if (!string.IsNullOrEmpty(query.Keyword)) {
            var keyword = query.Keyword.ToLowerInvariant();

            if (skill.Name.Equals(keyword, StringComparison.OrdinalIgnoreCase)) {
                score += 1.0;
            } else if (skill.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) {
                score += 0.8;
            }

            if (skill.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)) {
                score += 0.5;
            }

            if (query.FuzzyMatch && score == 0) {
                var fuzzyScore = CalculateFuzzyScore(skill.Name, keyword);
                score += fuzzyScore > 0.5 ? fuzzyScore * 0.4 : 0;
            }
        }

        if (query.Tags.Count > 0) {
            var tagMatches = query.Tags.Count(t =>
                skill.Tags.Any(st => st.Equals(t, StringComparison.OrdinalIgnoreCase)));
            if (tagMatches > 0) {
                score += 0.3 * ((double)tagMatches / query.Tags.Count);
            }
        }

        if (!string.IsNullOrEmpty(query.Category) &&
            string.Equals(skill.Namespace, query.Category, StringComparison.OrdinalIgnoreCase)) {
            score += 0.3;
        }

        return Math.Min(1.0, score);
    }

    private static double CalculateContextRelevance(SkillDefinition skill, IReadOnlyList<string> contextKeywords) {
        var skillText = $"{skill.Name} {skill.Description} {string.Join(" ", skill.Tags)}".ToLowerInvariant();

        var ac = AhoCorasick.Create(contextKeywords, ignoreCase: true);
        var matchedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in ac.FindAll(skillText.AsSpan()))
            matchedKeywords.Add(match.Value);

        var score = matchedKeywords.Count * 0.2;
        return Math.Min(1.0, score / Math.Max(1, contextKeywords.Count) * 2);
    }

    private static (SkillMatchType MatchType, string? Field) DetermineMatchType(SkillDefinition skill, SkillSearchQuery query) {
        if (string.IsNullOrEmpty(query.Keyword)) {
            if (query.Tags.Count > 0) {
                return (SkillMatchType.TagMatch, "Tags");
            }
            return (SkillMatchType.FuzzyMatch, null);
        }

        var keyword = query.Keyword.ToLowerInvariant();

        if (skill.Name.Equals(keyword, StringComparison.OrdinalIgnoreCase)) {
            return (SkillMatchType.ExactName, "Name");
        }

        if (skill.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) {
            return (SkillMatchType.PartialName, "Name");
        }

        if (query.Tags.Count > 0 && query.Tags.Any(t =>
            skill.Tags.Any(st => st.Equals(t, StringComparison.OrdinalIgnoreCase)))) {
            return (SkillMatchType.TagMatch, "Tags");
        }

        if (skill.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)) {
            return (SkillMatchType.DescriptionKeyword, "Description");
        }

        return (SkillMatchType.FuzzyMatch, null);
    }

    private static double CalculateFuzzyScore(string source, string target) {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0;

        source = source.ToLowerInvariant();
        target = target.ToLowerInvariant();

        if (source.Contains(target)) return 1.0;

        var sourceChars = source.ToCharArray();
        var targetChars = target.ToCharArray();
        var matchCount = 0;
        var sourceIndex = 0;

        foreach (var tc in targetChars) {
            while (sourceIndex < sourceChars.Length) {
                if (sourceChars[sourceIndex] == tc) {
                    matchCount++;
                    sourceIndex++;
                    break;
                }
                sourceIndex++;
            }
        }

        return (double)matchCount / targetChars.Length;
    }

    private static string? GenerateHighlight(SkillDefinition skill, SkillSearchQuery query) {
        if (string.IsNullOrEmpty(query.Keyword)) return null;

        var keyword = query.Keyword.ToLowerInvariant();
        var desc = skill.Description;

        var index = desc.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;

        var start = Math.Max(0, index - 20);
        var end = Math.Min(desc.Length, index + keyword.Length + 20);

        var prefix = start > 0 ? "..." : "";
        var suffix = end < desc.Length ? "..." : "";

        return $"{prefix}{desc[start..end]}{suffix}";
    }

    private static IReadOnlyList<string> ExtractKeywords(string context) {
        var words = context.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var stopWords = FrozenSet<string>.Empty;
        return words
            .Select(w => w.ToLowerInvariant().Trim(';', ',', '.', '!', '?', '(', ')', '[', ']', '{', '}'))
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct()
            .Take(20)
            .ToList();
    }

    /// <summary>
    /// 释放资源 — 释放索引锁
    /// </summary>
    public override void Dispose() {
        _indexLock.Dispose();
        base.Dispose();
    }

}