
namespace JoinCode.Abstractions.Interfaces;

public interface ISkillSearchService
{
    Task<IReadOnlyList<Models.SkillSearch.SkillSearchResult>> SearchAsync(Models.SkillSearch.SkillSearchQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.SkillSearch.SkillSearchResult>> RecommendAsync(string context, int maxResults = 5, CancellationToken cancellationToken = default);
}
