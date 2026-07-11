
namespace Core.Skills.Search;

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
