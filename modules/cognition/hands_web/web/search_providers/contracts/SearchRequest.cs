namespace Services.Web;

/// <summary>
/// 统一搜索请求 — 所有搜索引擎供应商的输入契约。
/// 由 <see cref="WebService.SearchAsync"/> 构造，传递给 <see cref="ISearchProvider.SearchAsync"/>。
/// </summary>
/// <param name="Query">搜索查询字符串，已由 WebService 层校验非空。</param>
/// <param name="AllowedDomains">可选域名白名单，仅返回这些域名的结果。与 <paramref name="BlockedDomains"/> 互斥。</param>
/// <param name="BlockedDomains">可选域名黑名单，排除这些域名的结果。与 <paramref name="AllowedDomains"/> 互斥。</param>
/// <param name="MaxResults">最大结果数，默认 10。供应商应尊重此上限，避免超量返回。</param>
public sealed record SearchRequest(
    string Query,
    string[]? AllowedDomains = null,
    string[]? BlockedDomains = null,
    int MaxResults = 10);
