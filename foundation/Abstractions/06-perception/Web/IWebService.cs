namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Web服务接口
/// </summary>
public interface IWebService
{
    /// <summary>
    /// 执行Web搜索（Anthropic服务端搜索或兼容搜索API）
    /// </summary>
    Task<WebSearchResult> SearchAsync(string query, string[]? allowedDomains = null, string[]? blockedDomains = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取网页内容
    /// </summary>
    Task<WebFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理WebFetch缓存（会话清理时调用）
    /// </summary>
    void ClearCache();
}

/// <summary>
/// 搜索结果项
/// </summary>
public sealed record SearchResultItem(
    string Title,
    string Url,
    string? Snippet = null,
    DateTime? PublishedDate = null);

/// <summary>
/// Web搜索结果
/// </summary>
public sealed record WebSearchResult(
    bool Success,
    string Query,
    List<SearchResultItem> Results,
    int TotalResults,
    double DurationSeconds = 0,
    string? ErrorMessage = null);

/// <summary>
/// Web内容获取结果 — 对齐TS版 WebFetchTool/utils.ts 的 getURLMarkdownContent 返回值
/// </summary>
public sealed record WebFetchResult(
    bool Success,
    string Url,
    string? Content = null,
    string? ContentType = null,
    int Bytes = 0,
    int StatusCode = 0,
    string? StatusText = null,
    bool Truncated = false,
    string? ErrorMessage = null,
    string? RedirectUrl = null,
    int RedirectStatusCode = 0,
    bool EgressBlocked = false,
    byte[]? RawBytes = null,
    string? PersistedPath = null,
    int PersistedSize = 0);
