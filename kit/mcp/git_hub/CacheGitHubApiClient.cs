namespace McpToolDispatch;

/// <summary>
/// IGitHubApiClient 装饰器 — GET 请求自动 LSM 缓存,可插拔
/// <para>缓存策略: GET + 非分页 → LSM 缓存(5min TTL,命中续期);POST/PATCH/PUT/DELETE 不缓存</para>
/// <para>可插拔: 传入 kvStore=null 则不缓存,透传内层 — 随时剥离缓存层</para>
/// <para>管道位置: GitHubApiClient → <b>CacheGitHubApiClient</b> → TimingGitHubApiClient → handler</para>
/// </summary>
internal sealed class CacheGitHubApiClient : IGitHubApiClient {
    private readonly IGitHubApiClient _inner;
    private readonly IKvStore? _kvStore;
    private readonly ILogger? _logger;

    /// <summary>GET 请求缓存 TTL — CI 数据频繁变化,5 分钟平衡命中率与新鲜度</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>创建缓存装饰器,包装内层 IGitHubApiClient</summary>
    /// <param name="inner">被包装的内层客户端(真实实现或下一层装饰器)</param>
    /// <param name="kvStore">可选 KV 缓存存储(LSM-Tree),null 时透传不缓存</param>
    /// <param name="logger">可选日志记录器,缓存异常时记录</param>
    public CacheGitHubApiClient(IGitHubApiClient inner, IKvStore? kvStore = null, ILogger? logger = null) {
        _inner = inner;
        _kvStore = kvStore;
        _logger = logger;
    }

    /// <summary>通用 REST API 调用 — GET+非分页自动缓存,其余透传内层</summary>
    public async Task<GitHubApiResponse> SendAsync(
        HttpMethod method, string path, string? body = null,
        IReadOnlyDictionary<string, string>? query = null,
        bool paginate = false, CancellationToken ct = default) {
        if (method != HttpMethod.Get || paginate || _kvStore is null)
            return await _inner.SendAsync(method, path, body, query, paginate, ct).ConfigureAwait(false);

        return await SendWithCacheAsync(path, query, ct).ConfigureAwait(false);
    }

    /// <summary>带缓存的 GET 请求 — 先读 LSM(命中续期),未命中调内层+写缓存</summary>
    private async Task<GitHubApiResponse> SendWithCacheAsync(
        string path, IReadOnlyDictionary<string, string>? query, CancellationToken ct) {
        var tracker = GhTimingTracker.CurrentTimer.Value;
        if (tracker?.CacheDisabled == true)
            return await _inner.SendAsync(HttpMethod.Get, path, null, query, false, ct).ConfigureAwait(false);

        var cacheKey = BuildCacheKey(path, query);
        var keyBytes = Encoding.UTF8.GetBytes(cacheKey);

        try {
            var readStart = Stopwatch.GetTimestamp();
            var cached = await _kvStore!.GetWithTtlAndRenewAsync(keyBytes, CacheTtl, ct).ConfigureAwait(false);
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

        var response = await _inner.SendAsync(HttpMethod.Get, path, null, query, false, ct).ConfigureAwait(false);

        if (response.Success) {
            try {
                var bytes = Encoding.UTF8.GetBytes(response.Body);
                var writeStart = Stopwatch.GetTimestamp();
                await _kvStore!.PutWithTtlAsync(keyBytes, bytes, CacheTtl, ct).ConfigureAwait(false);
                tracker?.AddLsmWrite(Stopwatch.GetTimestamp() - writeStart);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "gh 缓存写入失败 {Key}", cacheKey);
            }
        }

        return response;
    }

    /// <summary>构造缓存 key — gh:api:{path}?{sorted_query}</summary>
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
