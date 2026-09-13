namespace IO.Services;

/// <summary>
/// Release Notes 服务 — 拉取 GitHub Releases 并提供本地缓存
/// </summary>
[Register(typeof(IReleaseNotesService), ServiceLifetime.Singleton)]
public sealed partial class ReleaseNotesService : ServiceEntity, IReleaseNotesService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _cacheDuration;
    private readonly TimeProvider _timeProvider;

    private IReadOnlyList<ReleaseInfo> _cachedReleases = [];
    private bool _releasesCached;
    private DateTimeOffset _cacheTimestamp;
    private readonly AsyncLock _cacheLock = new();

    /// <summary>
    /// 构造 Release Notes 服务实例
    /// </summary>
    /// <param name="httpClient">用于访问 GitHub API 的 HTTP 客户端</param>
    /// <param name="repoOwner">GitHub 仓库所有者，缺省回退到默认仓库</param>
    /// <param name="repoName">GitHub 仓库名称，缺省回退到默认仓库</param>
    /// <param name="requestTimeout">请求超时时间，缺省 5 秒</param>
    /// <param name="cacheDuration">缓存有效期，缺省 1 小时</param>
    /// <param name="timeProvider">时间提供器，用于测试注入</param>
    public ReleaseNotesService(HttpClient httpClient, string? repoOwner = null, string? repoName = null,
        TimeSpan? requestTimeout = null, TimeSpan? cacheDuration = null, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _repoOwner = repoOwner ?? JccEndpointsResolver.RepoOwner;
        _repoName = repoName ?? JccEndpointsResolver.RepoName;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5);
        _cacheDuration = cacheDuration ?? TimeSpan.FromHours(1);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 异步获取最近的 Release 列表 — 优先走缓存，缓存失效时拉取 GitHub Releases API
    /// </summary>
    /// <param name="count">要获取的 Release 数量，缺省 5</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Release 信息列表；拉取失败且无缓存时返回空列表</returns>
    public async Task<IReadOnlyList<ReleaseInfo>> GetRecentReleasesAsync(int count = 5, CancellationToken ct = default)
    {
        if (TryGetCachedReleases(count, out var cached))
            return cached;

        using var cts = TimeoutHelper.CreateLinkedTimeout(ct, _requestTimeout);

        try
        {
            var url = $"{JccEndpointsResolver.GitHubApiBase}/repos/{_repoOwner}/{_repoName}/releases?per_page={count}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "JoinCode");

            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var doc = System.Text.Json.JsonDocument.Parse(json);

            var releases = new List<ReleaseInfo>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var tagName = element.GetProperty("tag_name").GetString() ?? "unknown";
                var body = element.GetProperty("body").GetString() ?? "";
                var publishedAt = element.GetProperty("published_at").GetDateTime();

                if (tagName.StartsWith('v'))
                    tagName = tagName[1..];

                releases.Add(new ReleaseInfo
                {
                    Version = tagName,
                    Notes = StringTruncator.Truncate(body, 503),
                    PublishedAt = publishedAt
                });
            }

            var result = releases.AsReadOnly();
            UpdateCache(result, ct);
            return result;
        }
        catch
        {
            if (TryGetCachedReleases(count, out var fallback))
                return fallback;

            return [];
        }
    }

    /// <summary>尝试从缓存获取 release 列表，缓存有效返回 true</summary>
    private bool TryGetCachedReleases(int count, out IReadOnlyList<ReleaseInfo> result)
    {
        using var guard = _cacheLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_cacheLock.Name}' 等待超时");
        if (_releasesCached && _timeProvider.GetUtcNow() - _cacheTimestamp < _cacheDuration)
        {
            result = _cachedReleases.Count <= count ? _cachedReleases : _cachedReleases.Take(count).ToList();
            return true;
        }
        result = [];
        return false;
    }

    /// <summary>更新 release 缓存</summary>
    private void UpdateCache(IReadOnlyList<ReleaseInfo> releases, CancellationToken ct)
    {
        using var guard = _cacheLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_cacheLock.Name}' 等待超时");
        _cachedReleases = releases;
        _releasesCached = true;
        _cacheTimestamp = _timeProvider.GetUtcNow();
    }

    /// <summary>释放资源 — P2-2: 补全 IDisposable 释放 SemaphoreSlim 避免资源累积</summary>
    protected override void OnDispose()
    {
        _cacheLock.Dispose();
    }

}
