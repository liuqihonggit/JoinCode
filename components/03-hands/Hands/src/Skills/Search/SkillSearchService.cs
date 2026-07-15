
namespace Core.Skills.Search;

[Register(typeof(ISkillSearchService))]
[Register(typeof(JoinCode.Abstractions.Interfaces.ISkillSearchService))]
public sealed partial class SkillSearchService : ISkillSearchService, JoinCode.Abstractions.Interfaces.ISkillSearchService
{
    private readonly ISkillService _skillService;
    [Inject] private readonly ILogger<SkillSearchService>? _logger;
    private readonly ConcurrentDictionary<string, FrozenSet<string>> _tagIndex = new();
    private readonly ConcurrentDictionary<string, string> _nameIndex = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastIndexTime = DateTime.MinValue;
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    public SkillSearchService(
        ISkillService skillService,
        ILogger<SkillSearchService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(skillService);
        _skillService = skillService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SkillSearchResult>> SearchAsync(
        SkillSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);

        var skills = await _skillService.GetAvailableSkillsAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SkillSearchResult>();

        foreach (var skill in skills)
        {
            var score = CalculateRelevanceScore(skill, query);
            if (score > 0)
            {
                var (matchType, matchedField) = DetermineMatchType(skill, query);
                results.Add(new SkillSearchResult
                {
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

    public async Task<IReadOnlyList<SkillSearchResult>> RecommendAsync(
        string context,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(context);

        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);

        var skills = await _skillService.GetAvailableSkillsAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SkillSearchResult>();

        var contextKeywords = ExtractKeywords(context);

        foreach (var skill in skills)
        {
            var score = CalculateContextRelevance(skill, contextKeywords);
            if (score > 0.2)
            {
                results.Add(new SkillSearchResult
                {
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

    public async IAsyncEnumerable<SkillSearchResult> SearchStreamAsync(
        SkillSearchQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);

        var skills = await _skillService.GetAvailableSkillsAsync(cancellationToken).ConfigureAwait(false);

        foreach (var skill in skills)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var score = CalculateRelevanceScore(skill, query);
            if (score > 0)
            {
                var (matchType, matchedField) = DetermineMatchType(skill, query);
                yield return new SkillSearchResult
                {
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

    private async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        if ((DateTime.UtcNow - _lastIndexTime).TotalMinutes < 5) return;

        await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if ((DateTime.UtcNow - _lastIndexTime).TotalMinutes < 5) return;

            var skills = await _skillService.GetAvailableSkillsAsync(cancellationToken).ConfigureAwait(false);
            _tagIndex.Clear();
            _nameIndex.Clear();

            foreach (var skill in skills)
            {
                _nameIndex[skill.Name] = skill.Name;
                if (skill.Tags.Count > 0)
                {
                    _tagIndex[skill.Name] = skill.Tags
                        .Select(t => t.ToLowerInvariant())
                        .ToFrozenSet();
                }
            }

            _lastIndexTime = DateTime.UtcNow;
            _logger?.LogDebug(L.T(StringKey.SkillSearchIndexRebuilt), skills.Count);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private static double CalculateRelevanceScore(SkillDefinition skill, SkillSearchQuery query)
    {
        var score = 0.0;

        if (!string.IsNullOrEmpty(query.Keyword))
        {
            var keyword = query.Keyword.ToLowerInvariant();

            if (skill.Name.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 1.0;
            }
            else if (skill.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.8;
            }

            if (skill.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.5;
            }

            if (query.FuzzyMatch && score == 0)
            {
                var fuzzyScore = CalculateFuzzyScore(skill.Name, keyword);
                if (fuzzyScore > 0.5)
                {
                    score += fuzzyScore * 0.4;
                }
            }
        }

        if (query.Tags.Count > 0)
        {
            var tagMatches = query.Tags.Count(t =>
                skill.Tags.Any(st => st.Equals(t, StringComparison.OrdinalIgnoreCase)));
            if (tagMatches > 0)
            {
                score += 0.3 * ((double)tagMatches / query.Tags.Count);
            }
        }

        if (!string.IsNullOrEmpty(query.Category) &&
            string.Equals(skill.Namespace, query.Category, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.3;
        }

        return Math.Min(1.0, score);
    }

    private static double CalculateContextRelevance(SkillDefinition skill, IReadOnlyList<string> contextKeywords)
    {
        var score = 0.0;
        var skillText = $"{skill.Name} {skill.Description} {string.Join(" ", skill.Tags)}".ToLowerInvariant();

        foreach (var keyword in contextKeywords)
        {
            if (skillText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.2;
            }
        }

        return Math.Min(1.0, score / Math.Max(1, contextKeywords.Count) * 2);
    }

    private static (SkillMatchType MatchType, string? Field) DetermineMatchType(SkillDefinition skill, SkillSearchQuery query)
    {
        if (string.IsNullOrEmpty(query.Keyword))
        {
            if (query.Tags.Count > 0)
            {
                return (SkillMatchType.TagMatch, "Tags");
            }
            return (SkillMatchType.FuzzyMatch, null);
        }

        var keyword = query.Keyword.ToLowerInvariant();

        if (skill.Name.Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return (SkillMatchType.ExactName, "Name");
        }

        if (skill.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return (SkillMatchType.PartialName, "Name");
        }

        if (query.Tags.Count > 0 && query.Tags.Any(t =>
            skill.Tags.Any(st => st.Equals(t, StringComparison.OrdinalIgnoreCase))))
        {
            return (SkillMatchType.TagMatch, "Tags");
        }

        if (skill.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return (SkillMatchType.DescriptionKeyword, "Description");
        }

        return (SkillMatchType.FuzzyMatch, null);
    }

    private static double CalculateFuzzyScore(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0;

        source = source.ToLowerInvariant();
        target = target.ToLowerInvariant();

        if (source.Contains(target)) return 1.0;

        var sourceChars = source.ToCharArray();
        var targetChars = target.ToCharArray();
        var matchCount = 0;
        var sourceIndex = 0;

        foreach (var tc in targetChars)
        {
            while (sourceIndex < sourceChars.Length)
            {
                if (sourceChars[sourceIndex] == tc)
                {
                    matchCount++;
                    sourceIndex++;
                    break;
                }
                sourceIndex++;
            }
        }

        return (double)matchCount / targetChars.Length;
    }

    private static string? GenerateHighlight(SkillDefinition skill, SkillSearchQuery query)
    {
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

    private static IReadOnlyList<string> ExtractKeywords(string context)
    {
        var words = context.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var stopWords = FrozenSet<string>.Empty;
        return words
            .Select(w => w.ToLowerInvariant().Trim(';', ',', '.', '!', '?', '(', ')', '[', ']', '{', '}'))
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct()
            .Take(20)
            .ToList();
    }

}
