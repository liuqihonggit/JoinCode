namespace McpToolDispatch;

/// <summary>
/// IGitHubApiClient 装饰器 — GET 请求 + GraphQL 查询自动 LSM 缓存,可插拔
/// <para>缓存策略: GET(含分页) → LSM 缓存;GraphQL query(非 mutation) → LSM 缓存;POST/PATCH/PUT/DELETE 不缓存</para>
/// <para>TTL 按端点区分: 搜索 2min,详情(数字ID结尾) 10min,默认 5min,GraphQL 5min</para>
/// <para>可插拔: 传入 kvStore=null 则不缓存,透传内层 — 随时剥离缓存层</para>
/// <para>管道位置: GitHubApiClient → <b>CacheGitHubApiClient</b> → TimingGitHubApiClient → handler</para>
/// </summary>
internal sealed class CacheGitHubApiClient : IGitHubApiClient {
    private readonly IGitHubApiClient _inner;
    private readonly IKvStore? _kvStore;
    private readonly ILogger? _logger;

    /// <summary>搜索端点 TTL — 搜索结果变化频繁,2 分钟</summary>
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(2);
    /// <summary>详情端点 TTL — 详情页变化少,10 分钟</summary>
    private static readonly TimeSpan DetailTtl = TimeSpan.FromMinutes(10);
    /// <summary>默认 TTL — 列表页等,5 分钟</summary>
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    /// <summary>GraphQL 查询 TTL — 5 分钟</summary>
    private static readonly TimeSpan GraphqlTtl = TimeSpan.FromMinutes(5);

    /// <summary>创建缓存装饰器,包装内层 IGitHubApiClient</summary>
    /// <param name="inner">被包装的内层客户端(真实实现或下一层装饰器)</param>
    /// <param name="kvStore">可选 KV 缓存存储(LSM-Tree),null 时透传不缓存</param>
    /// <param name="logger">可选日志记录器,缓存异常时记录</param>
    public CacheGitHubApiClient(IGitHubApiClient inner, IKvStore? kvStore = null, ILogger? logger = null) {
        _inner = inner;
        _kvStore = kvStore;
        _logger = logger;
    }

    /// <summary>通用 REST API 调用 — GET(含分页)自动缓存,GraphQL query 自动缓存,其余透传</summary>
    public async Task<GitHubApiResponse> SendAsync(
        HttpMethod method, string path, string? body = null,
        IReadOnlyDictionary<string, string>? query = null,
        bool paginate = false, CancellationToken ct = default) {
        if (_kvStore is null)
            return await _inner.SendAsync(method, path, body, query, paginate, ct).ConfigureAwait(false);

        var tracker = GhTimingTracker.CurrentTimer.Value;
        if (tracker?.CacheDisabled == true)
            return await _inner.SendAsync(method, path, body, query, paginate, ct).ConfigureAwait(false);

        if (method == HttpMethod.Get)
            return await CacheGetAsync(path, query, paginate, ct).ConfigureAwait(false);

        if (method == HttpMethod.Post && path == "graphql" && IsGraphQLQuery(body))
            return await CacheGraphqlAsync(body, ct).ConfigureAwait(false);

        return await _inner.SendAsync(method, path, body, query, paginate, ct).ConfigureAwait(false);
    }

    /// <summary>带缓存的 GET 请求 — 先读 LSM(命中续期),未命中调内层+写缓存</summary>
    private async Task<GitHubApiResponse> CacheGetAsync(
        string path, IReadOnlyDictionary<string, string>? query, bool paginate, CancellationToken ct) {
        var ttl = GetCacheTtl(path);
        var cacheKey = BuildCacheKey(path, query);
        var keyBytes = Encoding.UTF8.GetBytes(cacheKey);
        var tracker = GhTimingTracker.CurrentTimer.Value;

        if (await TryReadCacheAsync(keyBytes, ttl, cacheKey, tracker, ct).ConfigureAwait(false) is { } cached)
            return cached;

        var response = await _inner.SendAsync(HttpMethod.Get, path, null, query, paginate, ct).ConfigureAwait(false);
        await TryWriteCacheAsync(keyBytes, response, ttl, cacheKey, tracker, ct).ConfigureAwait(false);
        return response;
    }

    /// <summary>带缓存的 GraphQL 查询 — 缓存 key 用 body 的 SHA256 哈希前8位</summary>
    private async Task<GitHubApiResponse> CacheGraphqlAsync(string? body, CancellationToken ct) {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(body ?? string.Empty));
        var cacheKey = $"gh:graphql:{Convert.ToHexString(hash)[..8].ToLowerInvariant()}";
        var keyBytes = Encoding.UTF8.GetBytes(cacheKey);
        var tracker = GhTimingTracker.CurrentTimer.Value;

        if (await TryReadCacheAsync(keyBytes, GraphqlTtl, cacheKey, tracker, ct).ConfigureAwait(false) is { } cached)
            return cached;

        var response = await _inner.SendAsync(HttpMethod.Post, "graphql", body, null, false, ct).ConfigureAwait(false);
        await TryWriteCacheAsync(keyBytes, response, GraphqlTtl, cacheKey, tracker, ct).ConfigureAwait(false);
        return response;
    }

    /// <summary>尝试读缓存 — 命中返回响应,未命中返回 null</summary>
    private async Task<GitHubApiResponse?> TryReadCacheAsync(
        byte[] keyBytes, TimeSpan ttl, string cacheKey, GhTimingTracker? tracker, CancellationToken ct) {
        try {
            var readStart = Stopwatch.GetTimestamp();
            var cached = await _kvStore!.GetWithTtlAndRenewAsync(keyBytes, ttl, ct).ConfigureAwait(false);
            tracker?.AddLsmRead(Stopwatch.GetTimestamp() - readStart);
            if (cached is not null) {
                tracker?.RecordHit();
                return new GitHubApiResponse { Success = true, StatusCode = 200, Body = Encoding.UTF8.GetString(cached) };
            }
            tracker?.RecordMiss();
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh 缓存读取失败 {Key}", cacheKey);
            tracker?.RecordMiss();
        }
        return null;
    }

    /// <summary>尝试写缓存 — 成功响应写入 LSM</summary>
    private async Task TryWriteCacheAsync(
        byte[] keyBytes, GitHubApiResponse response, TimeSpan ttl, string cacheKey, GhTimingTracker? tracker, CancellationToken ct) {
        if (!response.Success) return;
        try {
            var bytes = Encoding.UTF8.GetBytes(response.Body);
            var writeStart = Stopwatch.GetTimestamp();
            await _kvStore!.PutWithTtlAsync(keyBytes, bytes, ttl, ct).ConfigureAwait(false);
            tracker?.AddLsmWrite(Stopwatch.GetTimestamp() - writeStart);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh 缓存写入失败 {Key}", cacheKey);
        }
    }

    /// <summary>按端点区分缓存 TTL — 搜索 2min,详情(数字ID结尾) 10min,默认 5min</summary>
    private static TimeSpan GetCacheTtl(string path) {
        if (path.Contains("/search/", StringComparison.Ordinal)) return SearchTtl;
        var lastSlash = path.LastIndexOf('/');
        if (lastSlash >= 0 && long.TryParse(path.AsSpan(lastSlash + 1), out _)) return DetailTtl;
        return DefaultTtl;
    }

    /// <summary>判断 GraphQL body 是否为查询(非 mutation) — body 含 "query" 且不含 "mutation"</summary>
    private static bool IsGraphQLQuery(string? body) {
        if (string.IsNullOrEmpty(body)) return false;
        return body.Contains("\"query\"", StringComparison.OrdinalIgnoreCase)
            && !body.Contains("mutation", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>构造 GET 缓存 key — gh:api:{path}?{sorted_query}</summary>
    private static string BuildCacheKey(string path, IReadOnlyDictionary<string, string>? query) {
        if (query is null || query.Count == 0) return $"gh:api:{path}";
        var sb = new StringBuilder($"gh:api:{path}?{query.Count * 16}");
        foreach (var (k, v) in query.OrderBy(x => x.Key, StringComparer.Ordinal))
            sb.Append(k).Append('=').Append(v).Append('&');
        sb.Length--;
        return sb.ToString();
    }

    /// <summary>获取 Run 日志 — 透传(日志缓存由 GitHubRunLogFilterRunner 管理)</summary>
    public IAsyncEnumerable<string> GetRunLogsAsync(string owner, string repo, long runId, CancellationToken ct = default)
        => _inner.GetRunLogsAsync(owner, repo, runId, ct);

    /// <summary>获取 Job 日志 — 透传(日志缓存由 GitHubRunLogFilterRunner 管理)</summary>
    public IAsyncEnumerable<string> GetJobLogsAsync(string owner, string repo, long jobId, CancellationToken ct = default)
        => _inner.GetJobLogsAsync(owner, repo, jobId, ct);

    /// <summary>上传 Release asset — 透传(上传不缓存)</summary>
    public Task<GitHubApiResponse> UploadAssetAsync(
        string owner, string repo, long releaseId, string fileName, Stream fileStream, CancellationToken ct = default)
        => _inner.UploadAssetAsync(owner, repo, releaseId, fileName, fileStream, ct);

    /// <summary>上传 Issue/PR 附件 — 透传(上传不缓存)</summary>
    public Task<GitHubApiResponse> UploadAttachmentAsync(
        long repositoryId, string fileName, Stream fileStream, CancellationToken ct = default)
        => _inner.UploadAttachmentAsync(repositoryId, fileName, fileStream, ct);

    /// <summary>下载 artifact — 透传(下载不缓存)</summary>
    public Task<GitHubApiResponse> DownloadArtifactAsync(
        string owner, string repo, long artifactId, string filePath, CancellationToken ct = default)
        => _inner.DownloadArtifactAsync(owner, repo, artifactId, filePath, ct);
}
