namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Facet 缓存服务 — 对齐 TS insights.ts loadCachedFacets + saveFacets
/// 缓存路径: ~/.jcc/usage-data/facets/{sessionId}.json
/// </summary>
public interface IFacetCacheService
{
    /// <summary>从缓存加载 SessionFacets，不存在返回 null</summary>
    Task<SessionFacets?> LoadAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>保存 SessionFacets 到缓存</summary>
    Task SaveAsync(SessionFacets facets, CancellationToken cancellationToken = default);

    /// <summary>检查缓存是否存在且有效</summary>
    Task<bool> IsValidAsync(string sessionId, CancellationToken cancellationToken = default);
}
