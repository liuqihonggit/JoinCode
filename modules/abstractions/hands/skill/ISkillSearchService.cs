
namespace JoinCode.Abstractions.Interfaces;

public interface ISkillSearchService {
    /// <summary>异步搜索技能。</summary>
    Task<IReadOnlyList<Models.SkillSearch.SkillSearchResult>> SearchAsync(Models.SkillSearch.SkillSearchQuery query, CancellationToken cancellationToken = default);
    /// <summary>异步推荐技能。</summary>
    Task<IReadOnlyList<Models.SkillSearch.SkillSearchResult>> RecommendAsync(string context, int maxResults = 5, CancellationToken cancellationToken = default);
}