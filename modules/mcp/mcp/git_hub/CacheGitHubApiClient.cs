// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace McpToolDispatch;

/// <summary>
/// IGitHubApiClient 装饰器 — GET 请求 + GraphQL 查询自动缓存,可插拔
/// <para>缓存策略: GET(含分页) → IGhCacheService 缓存;GraphQL query(非 mutation) → 缓存;POST/PATCH/PUT/DELETE 不缓存</para>
/// <para>可插拔: 传入 cacheService=null 则不缓存,透传内层 — 随时剥离缓存层</para>
/// <para>管道位置: GitHubApiClient → <b>CacheGitHubApiClient</b> → TimingGitHubApiClient → handler</para>
/// <para>key 构造和 TTL 策略委托给 IGhCacheService,本类只管缓存判断+计时</para>
/// </summary>
internal sealed class CacheGitHubApiClient : IGitHubApiClient {
    private readonly IGitHubApiClient _inner;
    private readonly IGhCacheService? _cacheService;
    private readonly ILogger? _logger;

    /// <summary>创建缓存装饰器,包装内层 IGitHubApiClient</summary>
    /// <param name="inner">被包装的内层客户端(真实实现或下一层装饰器)</param>
    /// <param name="cacheService">可选 gh 缓存服务,null 时透传不缓存</param>
    /// <param name="logger">可选日志记录器,缓存异常时记录</param>
    public CacheGitHubApiClient(IGitHubApiClient inner, IGhCacheService? cacheService = null, ILogger? logger = null) {
        _inner = inner;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>通用 REST API 调用 — GET(含分页)自动缓存,GraphQL query 自动缓存,其余透传</summary>
    public async Task<GitHubApiResponse> SendAsync(
        HttpMethod method, string path, string? body = null,
        IReadOnlyDictionary<string, string>? query = null,
        bool paginate = false, CancellationToken ct = default) {
        if (_cacheService is null)
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

    /// <summary>带缓存的 GET 请求 — 先读缓存(命中续期),未命中调内层+写缓存</summary>
    private async Task<GitHubApiResponse> CacheGetAsync(
        string path, IReadOnlyDictionary<string, string>? query, bool paginate, CancellationToken ct) {
        var tracker = GhTimingTracker.CurrentTimer.Value;
        var readStart = Stopwatch.GetTimestamp();
        var cached = await _cacheService!.TryGetApiAsync(path, query, ct).ConfigureAwait(false);
        tracker?.AddLsmRead(Stopwatch.GetTimestamp() - readStart);
        if (cached is not null) {
            tracker?.RecordHit();
            return new GitHubApiResponse { Success = true, StatusCode = 200, Body = cached };
        }
        tracker?.RecordMiss();

        var response = await _inner.SendAsync(HttpMethod.Get, path, null, query, paginate, ct).ConfigureAwait(false);
        if (response.Success) {
            var writeStart = Stopwatch.GetTimestamp();
            await _cacheService.WriteApiAsync(path, query, response.Body, ct).ConfigureAwait(false);
            tracker?.AddLsmWrite(Stopwatch.GetTimestamp() - writeStart);
        }
        return response;
    }

    /// <summary>带缓存的 GraphQL 查询 — 缓存 key 用 body 的 SHA256 哈希前8位</summary>
    private async Task<GitHubApiResponse> CacheGraphqlAsync(string? body, CancellationToken ct) {
        var tracker = GhTimingTracker.CurrentTimer.Value;
        var readStart = Stopwatch.GetTimestamp();
        var cached = await _cacheService!.TryGetGraphqlAsync(body ?? string.Empty, ct).ConfigureAwait(false);
        tracker?.AddLsmRead(Stopwatch.GetTimestamp() - readStart);
        if (cached is not null) {
            tracker?.RecordHit();
            return new GitHubApiResponse { Success = true, StatusCode = 200, Body = cached };
        }
        tracker?.RecordMiss();

        var response = await _inner.SendAsync(HttpMethod.Post, "graphql", body, null, false, ct).ConfigureAwait(false);
        if (response.Success) {
            var writeStart = Stopwatch.GetTimestamp();
            await _cacheService.WriteGraphqlAsync(body ?? string.Empty, response.Body, ct).ConfigureAwait(false);
            tracker?.AddLsmWrite(Stopwatch.GetTimestamp() - writeStart);
        }
        return response;
    }

    /// <summary>判断 GraphQL body 是否为查询(非 mutation) — body 含 "query" 且不含 "mutation"</summary>
    private static bool IsGraphQLQuery(string? body) {
        if (string.IsNullOrEmpty(body)) return false;
        return body.Contains("\"query\"", StringComparison.OrdinalIgnoreCase)
            && !body.Contains("mutation", StringComparison.OrdinalIgnoreCase);
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

/// <summary>
/// gh 缓存统一服务 — 封装所有 gh 缓存 key 构造 + TTL 策略,避免多处分散注入 IKvStore
/// <para>三层缓存: API级(gh:api:/gh:graphql:) + 日志级(gh:log:/gh:jobs:/gh:runid:) + 告警级(gh:ci_alert:)</para>
/// <para>可插拔: 传入 kvStore=null 则不缓存(所有 TryGet 返回 null,Write 为 no-op)</para>
/// <para>key 构造用 record struct 封装(JobLogCacheKey/PrRunIdCacheKey/ShaRunIdCacheKey),禁止裸字符串拼接</para>
/// </summary>
internal interface IGhCacheService {
    // === API 级缓存 ===
    /// <summary>尝试读取 API 缓存 — 命中返回 body,未命中返回 null</summary>
    Task<string?> TryGetApiAsync(string path, IReadOnlyDictionary<string, string>? query, CancellationToken ct);
    /// <summary>写入 API 缓存 — 按端点自动选择 TTL(search 2min/detail 10min/default 5min)</summary>
    Task WriteApiAsync(string path, IReadOnlyDictionary<string, string>? query, string body, CancellationToken ct);
    /// <summary>尝试读取 GraphQL 查询缓存 — 命中返回 body,未命中返回 null</summary>
    Task<string?> TryGetGraphqlAsync(string body, CancellationToken ct);
    /// <summary>写入 GraphQL 查询缓存 — TTL 5min</summary>
    Task WriteGraphqlAsync(string body, string responseBody, CancellationToken ct);

    // === job 日志缓存 ===
    /// <summary>尝试读取 job 日志缓存 — 命中返回 value 原始字节([runId(8)][expiry(8)][日志]),未命中返回 null</summary>
    Task<byte[]?> TryGetJobLogAsync(long jobId, CancellationToken ct);
    /// <summary>写入 job 日志缓存 — key=gh:log:{jobId},value=[runId(8)][expiry(8)][日志],TTL 7天</summary>
    Task WriteJobLogAsync(long jobId, long runId, byte[] logContent, CancellationToken ct);
    /// <summary>从 job 日志缓存反查 runId(value 头 8 字节)</summary>
    Task<long?> TryGetRunIdByJobIdAsync(long jobId, CancellationToken ct);

    // === run 日志缓存 ===
    /// <summary>尝试读取 run 日志缓存 — key=gh:log:{runId}:run,命中返回日志字节,未命中返回 null</summary>
    Task<byte[]?> TryGetRunLogAsync(string runId, CancellationToken ct);
    /// <summary>写入 run 日志缓存 — TTL 7天</summary>
    Task WriteRunLogAsync(string runId, byte[] logContent, CancellationToken ct);

    // === jobs 列表缓存 ===
    /// <summary>尝试读取 jobs 列表缓存 — key=gh:jobs:full:{runId},命中返回 JSON 字节,未命中返回 null</summary>
    Task<byte[]?> TryGetJobsAsync(long runId, CancellationToken ct);
    /// <summary>写入 jobs 列表缓存 — TTL 7天</summary>
    Task WriteJobsAsync(long runId, byte[] json, CancellationToken ct);

    // === 反查索引(前置表) ===
    /// <summary>反查 PR → run-id — key=gh:runid:pr:{prNumber},TTL 5min</summary>
    Task<string?> TryGetRunIdByPrAsync(int prNumber, CancellationToken ct);
    /// <summary>写入 PR → run-id 反查索引</summary>
    Task WriteRunIdByPrAsync(int prNumber, string runId, CancellationToken ct);
    /// <summary>反查 SHA → run-id — key=gh:runid:sha:{headSha},TTL 7天</summary>
    Task<string?> TryGetRunIdByShaAsync(string headSha, CancellationToken ct);
    /// <summary>写入 SHA → run-id 反查索引</summary>
    Task WriteRunIdByShaAsync(string headSha, string runId, CancellationToken ct);

    // === 告警缓存 ===
    /// <summary>扫描 CI 告警 — 范围扫描 gh:ci_alert: 前缀</summary>
    IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAlertsAsync(CancellationToken ct);
    /// <summary>写入 CI 告警 — key=gh:ci_alert:{runId}:{jobId}:{timestamp},无 TTL(永久)</summary>
    Task WriteAlertAsync(string key, byte[] value, CancellationToken ct);
    /// <summary>删除告警</summary>
    Task DeleteAlertAsync(byte[] key, CancellationToken ct);

    // === 缓存清理 ===
    /// <summary>扫描所有 gh 缓存 — 范围扫描 gh: 前缀</summary>
    IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAllGhAsync(CancellationToken ct);
    /// <summary>删除指定 key</summary>
    Task DeleteAsync(byte[] key, CancellationToken ct);
}

/// <summary>
/// gh 缓存统一服务实现 — 内部注入 IKvStore,集中管理所有 key 构造和 TTL 策略
/// <para>kvStore=null 时为 NoOp 实现(所有 TryGet 返回 null,Write 为 no-op),支持随时剥离缓存层</para>
/// </summary>
internal sealed class GhCacheService : IGhCacheService {
    private readonly IKvStore? _kvStore;
    private readonly ILogger? _logger;

    // === TTL 常量(集中定义) ===
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DetailTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GraphqlTtl = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan LogTtl = TimeSpan.FromDays(7);
    internal static readonly TimeSpan RunIdReverseLookupTtl = TimeSpan.FromMinutes(5);

    /// <summary>创建 gh 缓存服务 — kvStore=null 时为 NoOp(不缓存)</summary>
    public GhCacheService(IKvStore? kvStore, ILogger? logger) {
        _kvStore = kvStore;
        _logger = logger;
    }

    // === API 级缓存 ===
    /// <inheritdoc/>
    public async Task<string?> TryGetApiAsync(string path, IReadOnlyDictionary<string, string>? query, CancellationToken ct) {
        if (_kvStore is null) return null;
        var ttl = GetApiTtl(path);
        var keyBytes = BuildApiCacheKey(path, query);
        try {
            var cached = await _kvStore.GetWithTtlAndRenewAsync(keyBytes, ttl, ct).ConfigureAwait(false);
            return cached is null ? null : Encoding.UTF8.GetString(cached);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh API 缓存读取失败 path={Path}", path);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task WriteApiAsync(string path, IReadOnlyDictionary<string, string>? query, string body, CancellationToken ct) {
        if (_kvStore is null) return;
        var ttl = GetApiTtl(path);
        var keyBytes = BuildApiCacheKey(path, query);
        try {
            var bytes = Encoding.UTF8.GetBytes(body);
            await _kvStore.PutWithTtlAsync(keyBytes, bytes, ttl, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh API 缓存写入失败 path={Path}", path);
        }
    }

    /// <inheritdoc/>
    public async Task<string?> TryGetGraphqlAsync(string body, CancellationToken ct) {
        if (_kvStore is null) return null;
        var keyBytes = BuildGraphqlCacheKey(body);
        try {
            var cached = await _kvStore.GetWithTtlAndRenewAsync(keyBytes, GraphqlTtl, ct).ConfigureAwait(false);
            return cached is null ? null : Encoding.UTF8.GetString(cached);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh GraphQL 缓存读取失败");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task WriteGraphqlAsync(string body, string responseBody, CancellationToken ct) {
        if (_kvStore is null) return;
        var keyBytes = BuildGraphqlCacheKey(body);
        try {
            var bytes = Encoding.UTF8.GetBytes(responseBody);
            await _kvStore.PutWithTtlAsync(keyBytes, bytes, GraphqlTtl, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh GraphQL 缓存写入失败");
        }
    }

    // === job 日志缓存 ===
    /// <inheritdoc/>
    public async Task<byte[]?> TryGetJobLogAsync(long jobId, CancellationToken ct) {
        if (_kvStore is null) return null;
        var keyBytes = JobLogCacheKey.BuildCacheKey(jobId);
        try {
            return await _kvStore.GetWithTtlAndRenewAsync(keyBytes, LogTtl, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh job 日志缓存读取失败 jobId={JobId}", jobId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task WriteJobLogAsync(long jobId, long runId, byte[] logContent, CancellationToken ct) {
        if (_kvStore is null) return;
        var cacheEntry = new JobLogCacheKey(jobId, runId);
        var keyBytes = cacheEntry.ToCacheKeyBytes();
        var valueBytes = cacheEntry.ToValueBytes(logContent, DateTime.UtcNow + LogTtl);
        try {
            await _kvStore.PutWithTtlAsync(keyBytes, valueBytes, LogTtl, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh job 日志缓存写入失败 jobId={JobId}", jobId);
        }
    }

    /// <inheritdoc/>
    public async Task<long?> TryGetRunIdByJobIdAsync(long jobId, CancellationToken ct) {
        if (_kvStore is null) return null;
        var keyBytes = JobLogCacheKey.BuildCacheKey(jobId);
        try {
            var cached = await _kvStore.GetWithTtlAndRenewAsync(keyBytes, LogTtl, ct).ConfigureAwait(false);
            return JobLogCacheKey.TryExtractRunId(cached);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh job 日志缓存反查 runId 失败 jobId={JobId}", jobId);
            return null;
        }
    }

    // === run 日志缓存 ===
    /// <inheritdoc/>
    public async Task<byte[]?> TryGetRunLogAsync(string runId, CancellationToken ct) {
        if (_kvStore is null) return null;
        var keyBytes = Encoding.UTF8.GetBytes($"gh:log:{runId}:run");
        try {
            return await _kvStore.GetWithTtlAndRenewAsync(keyBytes, LogTtl, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh run 日志缓存读取失败 runId={RunId}", runId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task WriteRunLogAsync(string runId, byte[] logContent, CancellationToken ct) {
        if (_kvStore is null) return;
        var keyBytes = Encoding.UTF8.GetBytes($"gh:log:{runId}:run");
        try {
            await _kvStore.PutWithTtlAsync(keyBytes, logContent, LogTtl, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh run 日志缓存写入失败 runId={RunId}", runId);
        }
    }

    // === jobs 列表缓存 ===
    /// <inheritdoc/>
    public async Task<byte[]?> TryGetJobsAsync(long runId, CancellationToken ct) {
        if (_kvStore is null) return null;
        var keyBytes = Encoding.UTF8.GetBytes($"gh:jobs:full:{runId}");
        try {
            return await _kvStore.GetAsync(keyBytes, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh jobs 列表缓存读取失败 runId={RunId}", runId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task WriteJobsAsync(long runId, byte[] json, CancellationToken ct) {
        if (_kvStore is null) return;
        var keyBytes = Encoding.UTF8.GetBytes($"gh:jobs:full:{runId}");
        try {
            await _kvStore.PutWithTtlAsync(keyBytes, json, LogTtl, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh jobs 列表缓存写入失败 runId={RunId}", runId);
        }
    }

    // === 反查索引(前置表) ===
    /// <inheritdoc/>
    public async Task<string?> TryGetRunIdByPrAsync(int prNumber, CancellationToken ct) {
        if (_kvStore is null) return null;
        var key = new PrRunIdCacheKey(prNumber).ToCacheKeyBytes();
        try {
            var cached = await _kvStore.GetWithTtlAndRenewAsync(key, RunIdReverseLookupTtl, ct).ConfigureAwait(false);
            return cached is null ? null : Encoding.UTF8.GetString(cached);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh PR→runId 反查失败 prNumber={PrNumber}", prNumber);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task WriteRunIdByPrAsync(int prNumber, string runId, CancellationToken ct) {
        if (_kvStore is null) return;
        var key = new PrRunIdCacheKey(prNumber).ToCacheKeyBytes();
        var bytes = Encoding.UTF8.GetBytes(runId);
        try {
            await _kvStore.PutWithTtlAsync(key, bytes, RunIdReverseLookupTtl, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh PR→runId 反查写入失败 prNumber={PrNumber}", prNumber);
        }
    }

    /// <inheritdoc/>
    public async Task<string?> TryGetRunIdByShaAsync(string headSha, CancellationToken ct) {
        if (_kvStore is null) return null;
        var key = new ShaRunIdCacheKey(headSha).ToCacheKeyBytes();
        try {
            var cached = await _kvStore.GetWithTtlAndRenewAsync(key, LogTtl, ct).ConfigureAwait(false);
            return cached is null ? null : Encoding.UTF8.GetString(cached);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh SHA→runId 反查失败 headSha={HeadSha}", headSha);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task WriteRunIdByShaAsync(string headSha, string runId, CancellationToken ct) {
        if (_kvStore is null) return;
        var key = new ShaRunIdCacheKey(headSha).ToCacheKeyBytes();
        var bytes = Encoding.UTF8.GetBytes(runId);
        try {
            await _kvStore.PutWithTtlAsync(key, bytes, LogTtl, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh SHA→runId 反查写入失败 headSha={HeadSha}", headSha);
        }
    }

    // === 告警缓存 ===
    /// <inheritdoc/>
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAlertsAsync([EnumeratorCancellation] CancellationToken ct) {
        if (_kvStore is null) yield break;
        var fromKey = Encoding.UTF8.GetBytes("gh:ci_alert:");
        var toKey = Encoding.UTF8.GetBytes("gh:ci_alert:~");
        await foreach (var entry in _kvStore.ScanAsync(fromKey, toKey, ct).ConfigureAwait(false)) {
            yield return entry;
        }
    }

    /// <inheritdoc/>
    public async Task WriteAlertAsync(string key, byte[] value, CancellationToken ct) {
        if (_kvStore is null) return;
        var keyBytes = Encoding.UTF8.GetBytes(key);
        try {
            await _kvStore.PutAsync(keyBytes, value, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh 告警缓存写入失败 key={Key}", key);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAlertAsync(byte[] key, CancellationToken ct) {
        if (_kvStore is null) return;
        try {
            await _kvStore.DeleteAsync(key, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh 告警缓存删除失败");
        }
    }

    // === 缓存清理 ===
    /// <inheritdoc/>
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAllGhAsync([EnumeratorCancellation] CancellationToken ct) {
        if (_kvStore is null) yield break;
        var fromKey = Encoding.UTF8.GetBytes("gh:");
        var toKey = Encoding.UTF8.GetBytes("gh;");
        await foreach (var entry in _kvStore.ScanAsync(fromKey, toKey, ct).ConfigureAwait(false)) {
            yield return entry;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(byte[] key, CancellationToken ct) {
        if (_kvStore is null) return;
        try {
            await _kvStore.DeleteAsync(key, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "gh 缓存删除失败");
        }
    }

    // === 私有辅助: API 级 TTL + key 构造 ===
    private static TimeSpan GetApiTtl(string path) {
        if (path.Contains("/search/", StringComparison.Ordinal)) return SearchTtl;
        var lastSlash = path.LastIndexOf('/');
        if (lastSlash >= 0 && long.TryParse(path.AsSpan(lastSlash + 1), out _)) return DetailTtl;
        return DefaultTtl;
    }

    private static byte[] BuildApiCacheKey(string path, IReadOnlyDictionary<string, string>? query) {
        if (query is null || query.Count == 0) return Encoding.UTF8.GetBytes($"gh:api:{path}");
        var sb = new StringBuilder($"gh:api:{path}?{query.Count * 16}");
        foreach (var (k, v) in query.OrderBy(x => x.Key, StringComparer.Ordinal))
            sb.Append(k).Append('=').Append(v).Append('&');
        sb.Length--;
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] BuildGraphqlCacheKey(string body) {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(body ?? string.Empty));
        return Encoding.UTF8.GetBytes($"gh:graphql:{Convert.ToHexString(hash)[..8].ToLowerInvariant()}");
    }
}
