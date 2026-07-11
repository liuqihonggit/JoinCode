
namespace Core.Skills.Search;

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
