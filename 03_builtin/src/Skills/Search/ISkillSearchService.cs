
namespace Core.Skills.Search;

public interface ISkillSearchService
{
    Task<IReadOnlyList<SkillSearchResult>> SearchAsync(
        SkillSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkillSearchResult>> RecommendAsync(
        string context,
        int maxResults = 5,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<SkillSearchResult> SearchStreamAsync(
        SkillSearchQuery query,
        CancellationToken cancellationToken = default);
}
