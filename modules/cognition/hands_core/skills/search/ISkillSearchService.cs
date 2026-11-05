
namespace Core.Skills.Search;

/// <summary>
/// 技能搜索服务接口 — 提供技能搜索、推荐和流式搜索能力
/// </summary>
public interface ISkillSearchService {
    /// <summary>
    /// 异步搜索技能
    /// </summary>
    /// <param name="query">搜索查询</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>搜索结果列表</returns>
    Task<IReadOnlyList<SkillSearchResult>> SearchAsync(
        SkillSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 基于上下文异步推荐技能
    /// </summary>
    /// <param name="context">上下文描述</param>
    /// <param name="maxResults">最大返回结果数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>推荐结果列表</returns>
    Task<IReadOnlyList<SkillSearchResult>> RecommendAsync(
        string context,
        int maxResults = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式搜索技能
    /// </summary>
    /// <param name="query">搜索查询</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>搜索结果异步枚举</returns>
    IAsyncEnumerable<SkillSearchResult> SearchStreamAsync(
        SkillSearchQuery query,
        CancellationToken cancellationToken = default);
}