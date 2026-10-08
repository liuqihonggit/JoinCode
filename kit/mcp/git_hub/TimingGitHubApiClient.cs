namespace McpToolDispatch;

/// <summary>
/// IGitHubApiClient 装饰器 — 自动拦截网络请求耗时+GET 请求 LSM 缓存,写入 GhTimingTracker
/// <para>在 ExecuteGhAsync 中包装 _apiClient,所有 gh 工具自动获得网络计时+缓存,无需逐个 handler 打点</para>
/// <para>缓存策略: GET + 非分页 → LSM 缓存(5min TTL,命中续期);POST/PATCH/PUT/DELETE 不缓存</para>
/// <para>SendAsync 计网络耗时+缓存;GetRunLogsAsync/GetJobLogsAsync 透传(日志缓存+计时在 GitHubRunLogFilterRunner 中)</para>
/// </summary>
internal sealed class TimingGitHubApiClient : IGitHubApiClient {
    private readonly IGitHubApiClient _inner;
    private readonly IKvStore? _kvStore;
    private readonly ILogger? _logger;

    /// <summary>GET 请求缓存 TTL — CI 数据频繁变化,5 分钟平衡命中率与新鲜度</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>创建计时+缓存装饰器,包装内层 IGitHubApiClient</summary>
    /// <param name="inner">被包装的真实 API 客户端</param>
    /// <param name="kvStore">可选 KV 缓存存储(LSM-Tree),传入时 GET 请求自动缓存</param>
    /// <param name="logger">可选日志记录器,缓存异常时记录</param>
    public TimingGitHubApiClient(IGitHubApiClient inner, IKvStore? kvStore = null, ILogger? logger = null) {
        _inner = inner;
        _kvStore = kvStore;
        _logger = logger;
    }

    /// <summary>通用 REST API 调用 — GET+非分页自动缓存,所有请求自动计网络耗时</summary>
    public async Task<GitHubApiResponse> SendAsync(
        HttpMethod method, string path, string? body = null,
        IReadOnlyDictionary<string, string>? query = null,
        bool paginate = false, CancellationToken ct = default) {
        var tracker = GhTimingTracker.CurrentTimer.Value;

        if (method == HttpMethod.Get && !paginate && _kvStore is not null)
            return await SendWithCacheAsync(path, query, ct, tracker).ConfigureAwait(false);

        return await TimedSendAsync(method, path, body, query, paginate, ct, tracker).ConfigureAwait(false);
    }

    /// <summary>带缓存的 GET 请求 — 先读 LSM(命中续期),未命中调网络+写缓存</summary>
    private async Task<GitHubApiResponse> SendWithCacheAsync(
        string path, IReadOnlyDictionary<string, string>? query, CancellationToken ct, GhTimingTracker? tracker) {
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

        var response = await TimedSendAsync(HttpMethod.Get, path, null, query, false, ct, tracker).ConfigureAwait(false);

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

    /// <summary>计网络耗时的请求 — 无 tracker 时直接透传</summary>
    private async Task<GitHubApiResponse> TimedSendAsync(
        HttpMethod method, string path, string? body,
        IReadOnlyDictionary<string, string>? query, bool paginate,
        CancellationToken ct, GhTimingTracker? tracker) {
        if (tracker is null) return await _inner.SendAsync(method, path, body, query, paginate, ct).ConfigureAwait(false);
        var start = Stopwatch.GetTimestamp();
        try {
            return await _inner.SendAsync(method, path, body, query, paginate, ct).ConfigureAwait(false);
        } finally {
            tracker.AddNetwork(Stopwatch.GetTimestamp() - start);
        }
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

    /// <summary>获取 Run 日志 — 透传(日志下载计时在 GitHubRunLogFilterRunner 中)</summary>
    public IAsyncEnumerable<string> GetRunLogsAsync(string owner, string repo, long runId, CancellationToken ct = default)
        => _inner.GetRunLogsAsync(owner, repo, runId, ct);

    /// <summary>获取 Job 日志 — 透传(日志下载计时在 GitHubRunLogFilterRunner 中)</summary>
    public IAsyncEnumerable<string> GetJobLogsAsync(string owner, string repo, long jobId, CancellationToken ct = default)
        => _inner.GetJobLogsAsync(owner, repo, jobId, ct);

    /// <summary>上传 Release asset — 自动计网络耗时</summary>
    public async Task<GitHubApiResponse> UploadAssetAsync(
        string owner, string repo, long releaseId, string fileName, Stream fileStream, CancellationToken ct = default) {
        var tracker = GhTimingTracker.CurrentTimer.Value;
        if (tracker is null) return await _inner.UploadAssetAsync(owner, repo, releaseId, fileName, fileStream, ct).ConfigureAwait(false);
        var start = Stopwatch.GetTimestamp();
        try {
            return await _inner.UploadAssetAsync(owner, repo, releaseId, fileName, fileStream, ct).ConfigureAwait(false);
        } finally {
            tracker.AddNetwork(Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>上传 Issue/PR 附件 — 自动计网络耗时</summary>
    public async Task<GitHubApiResponse> UploadAttachmentAsync(
        long repositoryId, string fileName, Stream fileStream, CancellationToken ct = default) {
        var tracker = GhTimingTracker.CurrentTimer.Value;
        if (tracker is null) return await _inner.UploadAttachmentAsync(repositoryId, fileName, fileStream, ct).ConfigureAwait(false);
        var start = Stopwatch.GetTimestamp();
        try {
            return await _inner.UploadAttachmentAsync(repositoryId, fileName, fileStream, ct).ConfigureAwait(false);
        } finally {
            tracker.AddNetwork(Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>下载 artifact — 自动计网络耗时</summary>
    public async Task<GitHubApiResponse> DownloadArtifactAsync(
        string owner, string repo, long artifactId, string filePath, CancellationToken ct = default) {
        var tracker = GhTimingTracker.CurrentTimer.Value;
        if (tracker is null) return await _inner.DownloadArtifactAsync(owner, repo, artifactId, filePath, ct).ConfigureAwait(false);
        var start = Stopwatch.GetTimestamp();
        try {
            return await _inner.DownloadArtifactAsync(owner, repo, artifactId, filePath, ct).ConfigureAwait(false);
        } finally {
            tracker.AddNetwork(Stopwatch.GetTimestamp() - start);
        }
    }
}
