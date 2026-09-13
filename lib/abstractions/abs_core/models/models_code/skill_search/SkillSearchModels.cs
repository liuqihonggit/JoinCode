
namespace JoinCode.Abstractions.Models.SkillSearch;

public sealed class SkillSearchQuery
{
    public string? Keyword { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? Category { get; init; }
    public bool FuzzyMatch { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? ContextHint { get; init; }
}

public sealed class SkillSearchResult
{
    public required string SkillName { get; init; }
    public required string Description { get; init; }
    public required double RelevanceScore { get; init; }
    public required SkillMatchType MatchType { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? Category { get; init; }
    public string? MatchedField { get; init; }
    public string? Highlight { get; init; }
}

public enum SkillMatchType
{
    [EnumValue("exact_name")] ExactName = 0,
    [EnumValue("partial_name")] PartialName = 1,
    [EnumValue("tag_match")] TagMatch = 2,
    [EnumValue("description_keyword")] DescriptionKeyword = 3,
    [EnumValue("fuzzy_match")] FuzzyMatch = 4,
    [EnumValue("context_recommendation")] ContextRecommendation = 5
}
