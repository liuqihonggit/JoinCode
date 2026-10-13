namespace McpToolDispatch;

/// <summary>
/// job 日志缓存条目 — 只读记录类型封装 job-id + run-id + 淘汰时间,避免裸字符串拼接导致定位不清晰
/// <para>cache key: gh:log:{jobId}(纯 jobId 不拼接 runId)</para>
/// <para>value: [runId(8字节反查字段)][expiryTicks(8字节淘汰时间)][日志内容(UTF-8)]</para>
/// </summary>
internal readonly record struct JobLogCacheKey(long JobId, long RunId) {
    /// <summary>构造 LSM 缓存 key 字节(gh:log:{jobId}) — 静态,反查时无需 RunId</summary>
    public static byte[] BuildCacheKey(long jobId) => Encoding.UTF8.GetBytes($"gh:log:{jobId}");

    /// <summary>构造 LSM 缓存 key 字节(gh:log:{jobId}) — 实例,写入时用</summary>
    public byte[] ToCacheKeyBytes() => BuildCacheKey(JobId);

    /// <summary>构造 LSM 缓存 value 字节([runId(8)][expiryTicks(8)][日志内容])</summary>
    public byte[] ToValueBytes(ReadOnlySpan<byte> logContent, DateTime expiry) {
        var bytes = new byte[16 + logContent.Length];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 8), RunId);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 8), expiry.ToUniversalTime().Ticks);
        logContent.CopyTo(bytes.AsSpan(16));
        return bytes;
    }

    /// <summary>从缓存 value 反查 runId(value 头 8 字节)</summary>
    public static long? TryExtractRunId(byte[]? cached) {
        if (cached is null || cached.Length < 16) return null;
        return BitConverter.ToInt64(cached, 0);
    }

    /// <summary>从缓存 value 反查淘汰时间(value 第 9-16 字节)</summary>
    public static DateTime? TryExtractExpiry(byte[]? cached) {
        if (cached is null || cached.Length < 16) return null;
        return new DateTime(BitConverter.ToInt64(cached, 8), DateTimeKind.Utc);
    }
}

/// <summary>PR → run-id 反查索引 key — 只读记录类型封装 PR number,避免裸字符串拼接</summary>
/// <para>key=gh:runid:pr:{prNumber}, TTL 5 分钟(新 push 可能产生新 run)</para>
internal readonly record struct PrRunIdCacheKey(int PrNumber) {
    /// <summary>构造 LSM 缓存 key 字节</summary>
    public byte[] ToCacheKeyBytes() => Encoding.UTF8.GetBytes($"gh:runid:pr:{PrNumber}");
}

/// <summary>SHA → run-id 反查索引 key — 只读记录类型封装 head SHA,避免裸字符串拼接</summary>
/// <para>key=gh:runid:sha:{headSha}, TTL 7 天(SHA 固定,run-id 不变)</para>
internal readonly record struct ShaRunIdCacheKey(string HeadSha) {
    /// <summary>构造 LSM 缓存 key 字节</summary>
    public byte[] ToCacheKeyBytes() => Encoding.UTF8.GetBytes($"gh:runid:sha:{HeadSha}");
}

/// <summary>
/// GitHub Run 日志过滤运行器 — DI 注入 _apiClient/_cacheService,负责失败测试过滤+流式过滤+失败 job 日志获取
/// <para>日志缓存: 复用 IKvStore(LSM-Tree PithosKvStore),首次下载→后续命中缓存,避免重复下载</para>
/// </summary>
internal sealed class GitHubRunLogFilterRunner {
    private readonly IGitHubApiClient _apiClient;
    private readonly IGhCacheService? _cacheService;
    private readonly bool _compactLinePrefix;
    private readonly ILogger? _logger;

    /// <summary>缓存 TTL — 7 天后自动过期,由 KvStoreTtlCleanupService 定期清理</summary>
    internal static readonly TimeSpan CacheTtl = GhCacheService.LogTtl;

    /// <summary>
    /// 并行下载并发限制 — 从环境变量 JCC_GH_MAX_CONCURRENCY 读取,默认 16
    /// <para>用户可通过环境变量调整并发度,如 JCC_GH_MAX_CONCURRENCY=32</para>
    /// </summary>
    internal static int GetMaxConcurrency() {
        var env = Environment.GetEnvironmentVariable("JCC_GH_MAX_CONCURRENCY");
        if (int.TryParse(env, out var n) && n > 0) return n;
        return Environment.ProcessorCount + 1;
    }

    /// <summary>
    /// 构造日志过滤运行器,注入 GitHub API 客户端(非 null)+可选 gh 缓存服务
    /// </summary>
    /// <param name="apiClient">GitHub API 客户端(非 null)。</param>
    /// <param name="compactLinePrefix">行号前缀格式: true=紧凑 tab(行号\t内容), false=箭头(行号→内容)。对齐 FileOperationConfig.CompactLinePrefix</param>
    /// <param name="cacheService">可选 gh 缓存服务,用于日志缓存避免重复下载。</param>
    /// <param name="logger">可选日志记录器,缓存异常时记录</param>
    public GitHubRunLogFilterRunner(IGitHubApiClient apiClient, bool compactLinePrefix, IGhCacheService? cacheService = null, ILogger? logger = null) {
        _apiClient = apiClient;
        _cacheService = cacheService;
        _compactLinePrefix = compactLinePrefix;
        _logger = logger;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool LineMatchesAnyMarker(ReadOnlySpan<char> lineSpan, FrozenSet<string> markers) {
        foreach (var marker in markers) {
            if (lineSpan.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 统一日志流入口 — 根据 jobId/failedOnly 自动选择日志源,先入库 LSM 缓存再 yield
    /// <para>日志源: failedOnly=true → 失败 job 日志; jobId 有值 → 单/多 job 日志; 否则 → 整个 run 日志</para>
    /// <para>架构统一: 所有日志消费方(StreamAndFilter/FilterByRegex/FilterFailed)都通过此入口获取日志流</para>
    /// </summary>
    public IAsyncEnumerable<string> GetLogStreamAsync(
        string owner, string repo, string runId, string? jobId, bool failedOnly, bool wantRefresh, CancellationToken ct,
        int maxConcurrency = 0) {
        if (maxConcurrency <= 0) maxConcurrency = GetMaxConcurrency();
        if (failedOnly)
            return GetFailedJobLogsAsync(owner, repo, runId, wantRefresh, ct, maxConcurrency);
        if (!string.IsNullOrWhiteSpace(jobId)) {
            var jobIds = GitHubRunLogFilter.ParseJobIds(jobId);
            return DownloadJobsParallelAsync(owner, repo, runId, jobIds, wantRefresh, ct, maxConcurrency);
        }
        return GetOrFetchRunLogsAsync(owner, repo, runId, wantRefresh, ct);
    }

    /// <summary>
    /// 获取指定 Run 中所有失败 job 的日志 — 逐行 yield(合并多个 job 日志)
    /// <para>用于 expand=failed 模式,只拉 conclusion=failure 的 job 日志</para>
    /// <para>jobs list 缓存: key=gh:jobs:{runId},缓存失败 job ID 列表(已完成 run 的 job 列表不可变)</para>
    /// <para>wantRefresh=true 时跳过缓存读(仍写缓存),用于 rerun 后避免脏数据</para>
    /// </summary>
    public async IAsyncEnumerable<string> GetFailedJobLogsAsync(
        string owner, string repo, string runId, bool wantRefresh,
        [EnumeratorCancellation] CancellationToken ct, int maxConcurrency = 0) {
        if (maxConcurrency <= 0) maxConcurrency = GetMaxConcurrency();
        var failedJobIds = await GetOrFetchFailedJobIdsAsync(owner, repo, runId, wantRefresh, ct).ConfigureAwait(false);
        if (failedJobIds.Count == 0) yield break;

        await foreach (var line in DownloadJobsParallelAsync(owner, repo, runId, failedJobIds, wantRefresh, ct, maxConcurrency).ConfigureAwait(false)) {
            yield return line;
        }
    }

    /// <summary>
    /// 获取失败 job ID 列表 — 委托给 GetOrFetchJobsAsync 共享缓存(key=gh:jobs:full:{runId}),单次 API 调用
    /// <para>已完成 run 的 job 列表不可变,可安全缓存;wantRefresh=true 跳过缓存读(仍写缓存)</para>
    /// </summary>
    private async Task<List<long>> GetOrFetchFailedJobIdsAsync(string owner, string repo, string runId, bool wantRefresh, CancellationToken ct) {
        var jobs = await GetOrFetchJobsAsync(owner, repo, runId, wantRefresh, ct).ConfigureAwait(false);
        var failedJobIds = new List<long>(jobs.Count);
        foreach (var job in jobs) {
            if (job.Id == 0) continue;
            var severity = CiJobSeverityExtensions.FromConclusion(job.Conclusion, job.Status);
            if (severity.IsAnomaly())
                failedJobIds.Add(job.Id);
        }
        return failedJobIds;
    }

    /// <summary>
    /// 获取完整 job 列表 — 优先命中 LSM 缓存(key=gh:jobs:full:{runId}),未命中则调 API 并缓存
    /// <para>已完成 run 的 job 列表不可变,缓存 7 天;wantRefresh=true 跳过缓存读(仍写缓存)</para>
    /// <para>消除 BuildFailedJobsSummaryAsync/ExtractAllFailuresAsync/EnhanceDefaultViewWithJobsAsync 重复 API 调用</para>
    /// </summary>
    public async Task<List<RunJobItemResponse>> GetOrFetchJobsAsync(string owner, string repo, string runId, bool wantRefresh, CancellationToken ct) {
        var cacheKeyStr = $"gh:jobs:full:{runId}";
        var cacheKey = Encoding.UTF8.GetBytes(cacheKeyStr);
        var timer = GhTimingTracker.CurrentTimer.Value;
        _logger?.LogInformation("[KvDebug] GetOrFetchJobsAsync start: key={Key} cacheNull={KvNull} wantRefresh={Refresh}", cacheKeyStr, _cacheService is null, wantRefresh);

        if (!wantRefresh && _cacheService is not null) {
            if (await TryGetCachedJobsAsync(runId, cacheKeyStr, timer, ct).ConfigureAwait(false) is { } cachedJobs) {
                return cachedJobs;
            }
        }
        _logger?.LogInformation("[KvDebug] cache MISS: key={Key}", cacheKeyStr);
        timer?.RecordMiss();

        var tNet = Stopwatch.GetTimestamp();
        var jobsResult = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}/jobs", paginate: true, ct: ct).ConfigureAwait(false);
        timer?.AddNetwork(Stopwatch.GetTimestamp() - tNet);
        if (!jobsResult.Success) return [];

        try {
            var jobsResp = JsonSerializer.Deserialize(jobsResult.Body, GitHubApiJsonContext.Safe.RunJobListResponse);
            if (jobsResp?.Jobs is null) return [];
            var jobs = jobsResp.Jobs;
            if (_cacheService is not null) {
                var json = JsonSerializer.Serialize(jobsResp, GitHubApiJsonContext.Safe.RunJobListResponse);
                var jsonBytes = Encoding.UTF8.GetBytes(json);
                var tWrite = Stopwatch.GetTimestamp();
                await _cacheService.WriteJobsAsync(long.Parse(runId), jsonBytes, ct).ConfigureAwait(false);
                timer?.AddLsmWrite(Stopwatch.GetTimestamp() - tWrite);
            }
            return jobs;
        } catch (JsonException ex) {
            _logger?.LogWarning(ex, "解析 jobs 列表 JSON 失败");
            return [];
        }
    }

    /// <summary>尝试从缓存读取 jobs 列表 — 卫语句扁平化,避免 if 嵌套超 2 层</summary>
    private async Task<List<RunJobItemResponse>?> TryGetCachedJobsAsync(string runId, string cacheKeyStr, GhTimingTracker? timer, CancellationToken ct) {
        if (_cacheService is null) return null;
        var cached = await _cacheService.TryGetJobsAsync(long.Parse(runId), ct).ConfigureAwait(false);
        if (cached is null || cached.Length <= 8) return null;
        var expiryTicks = BitConverter.ToInt64(cached, 0);
        if (DateTime.UtcNow > new DateTime(expiryTicks, DateTimeKind.Utc)) return null;
        timer?.RecordHit();
        try {
            var resp = JsonSerializer.Deserialize(Encoding.UTF8.GetString(cached, 8, cached.Length - 8), GitHubApiJsonContext.Safe.RunJobListResponse);
            if (resp?.Jobs is not null) {
                _logger?.LogInformation("[KvDebug] cache HIT: key={Key} jobsCount={Count}", cacheKeyStr, resp.Jobs.Count);
                return resp.Jobs;
            }
        } catch (JsonException ex) { _logger?.LogWarning(ex, "解析缓存 jobs 列表 JSON 失败"); }
        return null;
    }


    /// <summary>
    /// 统一多 job 日志下载 — 单 job 走缓存,多 job Channel 并行合并(Actor 邮箱模型)
    /// <para>并行时各 job 行交错合并到 Channel,总时间 ≈ max(各 job) 而非 sum</para>
    /// <para>AGENTS.md 死锁处理规范: Actor 邮箱模型(消息传递替代共享锁)</para>
    /// <para>日志缓存: 复用 IKvStore(LSM-Tree),key=gh:log:{jobId}(纯 jobId),value=[runId(8字节)][日志],首次下载→后续命中</para>
    /// <para>wantRefresh=true 时跳过缓存读(仍写缓存),用于 rerun 后避免脏数据</para>
    /// </summary>
    public async IAsyncEnumerable<string> DownloadJobsParallelAsync(
        string owner, string repo, string runId, IReadOnlyList<long> jobIds, bool wantRefresh,
        [EnumeratorCancellation] CancellationToken ct, int maxConcurrency = 0) {
        if (maxConcurrency <= 0) maxConcurrency = GetMaxConcurrency();
        if (jobIds.Count == 0) yield break;

        // 单 job: 走缓存(避免 Channel 开销)
        if (jobIds.Count == 1) {
            await foreach (var line in GetOrFetchJobLogsAsync(owner, repo, runId, jobIds[0], wantRefresh, ct).ConfigureAwait(false)) {
                yield return line;
            }
            yield break;
        }

        // 多 job: Channel 并行合并(Actor 邮箱模型),每个 job 走缓存
        // 并发限制参数化,避免 GitHub API 二级限速(对齐旧系统 TryDownloadJobsAsync)
        var channel = Channel.CreateUnbounded<string>();
        var writer = channel.Writer;
        using var semaphore = new SemaphoreSlim(maxConcurrency);

        var tasks = jobIds.Select(async jobId => {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try {
                await foreach (var line in GetOrFetchJobLogsAsync(owner, repo, runId, jobId, wantRefresh, ct).ConfigureAwait(false)) {
                    await writer.WriteAsync(line, ct).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
                // 取消: 静默退出,channel 由外部完成
            } catch (Exception ex) {
                await writer.WriteAsync($"[ERROR] job {jobId}: {ex.Message}", CancellationToken.None).ConfigureAwait(false);
            } finally {
                semaphore.Release();
            }
        }).ToArray();

        // 后台等待所有完成后关闭 channel(不阻塞调用方 yield)
        _ = Task.Run(async () => {
            try {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            } finally {
                writer.TryComplete();
            }
        });

        await foreach (var line in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            yield return line;
        }
    }

    /// <summary>
    /// 获取单个 job 日志 — 优先命中 IKvStore(LSM-Tree)缓存,未命中则下载并写入缓存
    /// <para>key=gh:log:{jobId}(纯 jobId 不拼接 runId),value=[runId(8字节反查字段)][日志内容(UTF-8)]</para>
    /// <para>runId 存 value 头部供 TryGetRunIdByJobIdAsync 反查;淘汰时间由 LSM 内置 TTL 管理</para>
    /// <para>CI job 日志只读不可变: 首次写入后不再覆盖,仅 wantRefresh 强制刷新可覆盖</para>
    /// <para>wantRefresh=true 时跳过缓存读(仍写缓存),用于 rerun 后避免脏数据</para>
    /// </summary>
    public async IAsyncEnumerable<string> GetOrFetchJobLogsAsync(
        string owner, string repo, string runId, long jobId, bool wantRefresh,
        [EnumeratorCancellation] CancellationToken ct) {
        var cacheEntry = new JobLogCacheKey(jobId, long.Parse(runId));
        var cacheKey = cacheEntry.ToCacheKeyBytes();
        var timer = GhTimingTracker.CurrentTimer.Value;

        // 1. 查 LSM 缓存(wantRefresh 时跳过)
        if (!wantRefresh && _cacheService is not null) {
            var t0 = Stopwatch.GetTimestamp();
            var cached = await _cacheService.TryGetJobLogAsync(jobId, ct).ConfigureAwait(false);
            timer?.AddLsmRead(Stopwatch.GetTimestamp() - t0);
            if (cached is not null && cached.Length > 16) {
                timer?.RecordHit();
                var t1 = Stopwatch.GetTimestamp();
                foreach (var line in ParseCachedLines(cached, 16)) yield return line;
                timer?.AddParse(Stopwatch.GetTimestamp() - t1);
                yield break;
            }
        }
        timer?.RecordMiss();

        // 2. 缓存未命中: 下载所有行到 List
        var lines = new List<string>(256);
        var tNet = Stopwatch.GetTimestamp();
        await foreach (var line in _apiClient.GetJobLogsAsync(owner, repo, jobId, ct).ConfigureAwait(false)) {
            lines.Add(line);
        }
        timer?.AddNetwork(Stopwatch.GetTimestamp() - tNet);

        // 3. 写入 LSM 缓存(value=[runId(8)][expiry(8)][日志内容],CI job 日志只读不可变)
        if (_cacheService is not null && lines.Count > 0) {
            var text = string.Join('\n', lines);
            var logBytes = Encoding.UTF8.GetBytes(text);
            var tWrite = Stopwatch.GetTimestamp();
            await _cacheService.WriteJobLogAsync(jobId, long.Parse(runId), logBytes, ct).ConfigureAwait(false);
            timer?.AddLsmWrite(Stopwatch.GetTimestamp() - tWrite);
        }

        // 4. 逐行 yield
        foreach (var line in lines) {
            yield return line;
        }
    }

    /// <summary>
    /// 从 job 日志缓存反查 runId — key=gh:log:{jobId},value 头 8 字节为 runId(long)
    /// <para>用于从 jobId 反查所属 runId(如告警只存 jobId 时定位 run)</para>
    /// </summary>
    public async Task<long?> TryGetRunIdByJobIdAsync(long jobId, CancellationToken ct) {
        if (_cacheService is null) return null;
        return await _cacheService.TryGetRunIdByJobIdAsync(jobId, ct).ConfigureAwait(false);
    }

    /// <summary>反查索引 TTL — PR→run-id 映射可能随新 push 变化,5 分钟过期;SHA→run-id 固定用 CacheTtl(7天)</summary>
    internal static readonly TimeSpan RunIdReverseLookupTtl = TimeSpan.FromMinutes(5);

    /// <summary>反查 PR → run-id — key=gh:runid:pr:{prNumber},命中则跳过 API 调用</summary>
    public async Task<string?> TryGetRunIdByPrAsync(int prNumber, CancellationToken ct) {
        if (_cacheService is null) return null;
        return await _cacheService.TryGetRunIdByPrAsync(prNumber, ct).ConfigureAwait(false);
    }

    /// <summary>写入 PR → run-id 反查索引 — key=gh:runid:pr:{prNumber},TTL 5 分钟(新 push 可能产生新 run)</summary>
    public async Task WriteRunIdByPrAsync(int prNumber, string runId, CancellationToken ct) {
        if (_cacheService is null) return;
        await _cacheService.WriteRunIdByPrAsync(prNumber, runId, ct).ConfigureAwait(false);
    }

    /// <summary>反查 SHA → run-id — key=gh:runid:sha:{headSha},命中则跳过 API 调用</summary>
    public async Task<string?> TryGetRunIdByShaAsync(string headSha, CancellationToken ct) {
        if (_cacheService is null) return null;
        return await _cacheService.TryGetRunIdByShaAsync(headSha, ct).ConfigureAwait(false);
    }

    /// <summary>写入 SHA → run-id 反查索引 — key=gh:runid:sha:{headSha},TTL 7 天(SHA 固定,run-id 不变)</summary>
    public async Task WriteRunIdByShaAsync(string headSha, string runId, CancellationToken ct) {
        if (_cacheService is null) return;
        await _cacheService.WriteRunIdByShaAsync(headSha, runId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取整个 Run 日志 — 优先命中 IKvStore(LSM-Tree)缓存,未命中则下载并写入缓存
    /// <para>key=gh:log:{runId}:run,value=日志全文(UTF-8),首次下载→后续命中避免重复下载</para>
    /// <para>wantRefresh=true 时跳过缓存读(仍写缓存),用于 rerun 后避免脏数据</para>
    /// </summary>
    public async IAsyncEnumerable<string> GetOrFetchRunLogsAsync(
        string owner, string repo, string runId, bool wantRefresh,
        [EnumeratorCancellation] CancellationToken ct) {
        var cacheKey = Encoding.UTF8.GetBytes($"gh:log:{runId}:run");
        var timer = GhTimingTracker.CurrentTimer.Value;

        // 1. 查 LSM 缓存(wantRefresh 时跳过)
        if (!wantRefresh && _cacheService is not null) {
            var t0 = Stopwatch.GetTimestamp();
            var cached = await _cacheService.TryGetRunLogAsync(runId, ct).ConfigureAwait(false);
            timer?.AddLsmRead(Stopwatch.GetTimestamp() - t0);
            if (cached is not null) {
                timer?.RecordHit();
                var t1 = Stopwatch.GetTimestamp();
                foreach (var line in ParseCachedLines(cached)) yield return line;
                timer?.AddParse(Stopwatch.GetTimestamp() - t1);
                yield break;
            }
        }
        timer?.RecordMiss();

        // 2. 缓存未命中: 下载所有行到 List
        var lines = new List<string>(256);
        var tNet = Stopwatch.GetTimestamp();
        await foreach (var line in _apiClient.GetRunLogsAsync(owner, repo, long.Parse(runId), ct).ConfigureAwait(false)) {
            lines.Add(line);
        }
        timer?.AddNetwork(Stopwatch.GetTimestamp() - tNet);

        // 3. 写入 LSM 缓存
        if (_cacheService is not null && lines.Count > 0) {
            var text = string.Join('\n', lines);
            var bytes = Encoding.UTF8.GetBytes(text);
            var tWrite = Stopwatch.GetTimestamp();
            await _cacheService.WriteRunLogAsync(runId, bytes, ct).ConfigureAwait(false);
            timer?.AddLsmWrite(Stopwatch.GetTimestamp() - tWrite);
        }

        // 4. 逐行 yield
        foreach (var line in lines) {
            yield return line;
        }
    }

    /// <summary>解析缓存字节为逐行字符串(跳过空行);offset 跳过 value 头部字段(如 runId 8 字节)</summary>
    /// <para>索引遍历替代 Split,省掉 string[] 数组分配(极少 GC);IndexOf SIMD 加速</para>
    private static IEnumerable<string> ParseCachedLines(byte[] cached, int offset = 0) {
        var text = Encoding.UTF8.GetString(cached, offset, cached.Length - offset);
        var start = 0;
        while (start < text.Length) {
            var newlineIdx = text.IndexOf('\n', start);
            if (newlineIdx < 0) {
                if (start < text.Length) yield return text[start..];
                yield break;
            }
            if (newlineIdx > start) yield return text[start..newlineIdx];
            start = newlineIdx + 1;
        }
    }

    /// <summary>
    /// 从 LSM 缓存读取日志并解析为 RunLogSummary — 复用 DownloadJobsParallelAsync/GetOrFetchRunLogsAsync(LSM 缓存)
    /// <para>替代旧 GitHubRunLogCache.GetOrFetchSummaryAsync(MemoryCache+文件三级缓存)</para>
    /// <para>日志已在 LSM 中(本地磁盘),GitHubLogParser 解析是纯 CPU(O(n) 行数),无需单独缓存 summary</para>
    /// </summary>
    public async Task<RunLogSummary?> GetOrFetchSummaryAsync(
        string owner, string repo, string runId, string? jobId,
        bool wantRefresh, CancellationToken ct) {
        var (summary, _) = await ParseLogsToSummaryAsync(owner, repo, runId, jobId, wantRefresh, ct).ConfigureAwait(false);
        return summary;
    }

    /// <summary>
    /// 从 LSM 缓存读取日志并解析指定 section 的行列表 — 复用 LSM 缓存,实时解析
    /// <para>替代旧 GitHubRunLogCache.GetOrFetchSectionAsync(MemoryCache→Level1 填充→文件 raw 补填)</para>
    /// </summary>
    public async Task<List<string>?> GetOrFetchSectionAsync(
        string owner, string repo, string runId, string? jobId,
        string stepName, string sectionType,
        bool wantRefresh, CancellationToken ct) {
        var (_, sections) = await ParseLogsToSummaryAsync(owner, repo, runId, jobId, wantRefresh, ct).ConfigureAwait(false);
        if (!sections.TryGetValue(stepName, out var secs)) {
            var fuzzy = GitHubRunStepExtractor.TryFuzzyMatchStepName(sections.Keys, stepName);
            if (fuzzy is null) return null;
            stepName = fuzzy;
            secs = sections[fuzzy];
        }
        return secs.TryGetValue(sectionType, out var secLines) ? secLines : null;
    }

    /// <summary>
    /// 统一日志下载+解析 — 从 LSM 缓存读日志行,GitHubLogParser 解析为 summary + sectionContents
    /// <para>jobId 为空→整个 run 日志;jobId 有值→指定 job(s)(支持逗号分隔并行下载)</para>
    /// </summary>
    private async Task<(RunLogSummary summary, Dictionary<string, Dictionary<string, List<string>>> sections)> ParseLogsToSummaryAsync(
        string owner, string repo, string runId, string? jobId, bool wantRefresh, CancellationToken ct) {
        var summary = new RunLogSummary { RunId = runId, JobId = jobId };
        var sectionContents = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        var parser = new GitHubLogParser();
        var timer = GhTimingTracker.CurrentTimer.Value;

        var logLines = GetLogStreamAsync(owner, repo, runId, jobId, false, wantRefresh, ct);

        var lineNumber = 0;
        await foreach (var line in logLines.ConfigureAwait(false)) {
            var tParse = Stopwatch.GetTimestamp();
            parser.ParseLine(line, ++lineNumber, summary, sectionContents);
            timer?.AddParse(Stopwatch.GetTimestamp() - tParse);
        }

        return (summary, sectionContents);
    }

    /// <summary>
    /// 智能过滤测试失败行 — 状态机提取 Failed + Error Message + Stack Trace,Rust 风格输出
    /// <para>状态机: Normal → InFailedTest(遇到 Failed/[FAIL]) → InErrorMessage(Error Message:) → InStackTrace(Stack Trace:) → Normal</para>
    /// <para>输出: 每个失败测试用 --> line N 指示, | 管道符标注日志行, = 总结行</para>
    /// </summary>
    public async Task<ToolResult> FilterFailedTestsAsync(
        string owner, string repo, string runId, string? jobId,
        int maxLines, int skipLines, bool wantRefresh, CancellationToken ct) {
        // 统一日志流入口 — jobId 为空时 failedOnly=true(只拉失败 job),否则按 jobId 拉指定 job
        var failedOnly = string.IsNullOrWhiteSpace(jobId);
        var logLines = GetLogStreamAsync(owner, repo, runId, jobId, failedOnly, wantRefresh, ct);

        // 状态机解析
        var failures = new List<TestFailureInfo>();
        TestFailureInfo? current = null;
        var state = LogParseState.Normal;
        var lineNumber = 0;

        await foreach (var line in logLines.ConfigureAwait(false)) {
            lineNumber++;
            var content = GitHubRunLogText.StripLogTimestamp(line);
            ProcessLogLine(content, lineNumber, ref state, ref current, failures);
        }

        if (failures.Count == 0) {
            return GitHubToolHandlers.Ok("未检测到测试失败行。尝试用 filter=error 看 ##[error] 标记,或 log=true 看完整日志。", $"Run {runId} 测试失败过滤(0 个):");
        }

        // 去重: 同一测试名可能被 [xUnit.net] [FAIL] 和 Failed 两次报告,保留有 ErrorMessage 的那个
        failures = DeduplicateFailures(failures);

        return FormatFailuresRustStyle(failures, maxLines, skipLines, runId);
    }

    /// <summary>
    /// 并行提取多个 job 的失败测试信息 — Channel 流水线模型,下载+解析重叠
    /// <para>生产者: SemaphoreSlim 控制并发下载+解析,完成即推 channel</para>
    /// <para>消费者: 流式收集结果到 Dictionary,不等所有 job 完成</para>
    /// <para>计时: 返回 per-job 耗时(ms),供调用方诊断瓶颈(下载慢 vs 解析慢)</para>
    /// </summary>
    internal async Task<(Dictionary<long, List<TestFailureInfo>> results, string timing)> ExtractFailuresForJobsAsync(
        string owner, string repo, string runId, IReadOnlyList<long> jobIds,
        int maxConcurrency, bool wantRefresh, CancellationToken ct) {
        if (jobIds.Count == 0) return (new(), "");

        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var channel = Channel.CreateUnbounded<(long jobId, List<TestFailureInfo> failures, long elapsedMs)>();
        var writer = channel.Writer;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 生产者: 并行下载+解析,完成即推 channel(流水线: 下载完一个就推,不等其他)
        var producerTasks = jobIds.Select(async jobId => {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try {
                var jobSw = System.Diagnostics.Stopwatch.StartNew();
                var failures = await ExtractFailuresForJobAsync(
                    owner, repo, runId, jobId, wantRefresh, ct).ConfigureAwait(false);
                jobSw.Stop();
                await writer.WriteAsync((jobId, failures, jobSw.ElapsedMilliseconds), ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // 取消: 静默退出
            } catch (Exception) {
                await writer.WriteAsync((jobId, new List<TestFailureInfo>(), -1), CancellationToken.None).ConfigureAwait(false);
            } finally {
                semaphore.Release();
            }
        }).ToArray();

        // 后台等待所有生产者完成后关闭 channel
        _ = Task.Run(async () => {
            try { await Task.WhenAll(producerTasks).ConfigureAwait(false); }
            finally { writer.TryComplete(); }
        });

        // 消费者: 流式收集结果(一个 job 完成就立即入库,不等其他)
        var results = new Dictionary<long, List<TestFailureInfo>>(jobIds.Count);
        var timingSb = new StringBuilder();
        var maxMs = 0L;
        var slowJobs = new List<(long jobId, long ms)>();

        await foreach (var (jobId, failures, elapsedMs) in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            results[jobId] = failures;
            if (elapsedMs > maxMs) maxMs = elapsedMs;
            if (elapsedMs > 5000) slowJobs.Add((jobId, elapsedMs));
            timingSb.Append($"job {jobId}: {elapsedMs}ms, ");
        }

        sw.Stop();
        timingSb.Append($"\n总耗时 {sw.ElapsedMilliseconds}ms, 并发 {maxConcurrency}, 最慢 job {maxMs}ms");
        if (slowJobs.Count > 0)
            timingSb.Append($", 慢 job(>5s): {string.Join(", ", slowJobs.Select(s => $"{s.jobId}={s.ms}ms"))}");

        return (results, timingSb.ToString());
    }

    /// <summary>
    /// 提取指定 job 的失败测试信息 — 返回结构化 TestFailureInfo 列表(供 expand=failures 聚合)
    /// <para>复用 ProcessLogLine 状态机,对单个 job 日志提取 Failed+Error+StackTrace</para>
    /// </summary>
    internal async Task<List<TestFailureInfo>> ExtractFailuresForJobAsync(
        string owner, string repo, string runId, long jobId, bool wantRefresh, CancellationToken ct) {
        var failures = new List<TestFailureInfo>();
        TestFailureInfo? current = null;
        var state = LogParseState.Normal;
        var lineNumber = 0;

        var logLines = GetLogStreamAsync(owner, repo, runId, jobId.ToString(), false, wantRefresh, ct);
        await foreach (var line in logLines.ConfigureAwait(false)) {
            lineNumber++;
            var content = GitHubRunLogText.StripLogTimestamp(line);
            ProcessLogLine(content, lineNumber, ref state, ref current, failures);
        }

        return DeduplicateFailures(failures);
    }


    /// <summary>
    /// 流式拉取 + 过滤 + 分页跳过 — 全部日志源走 LSM 缓存,wantRefresh=true 跳过缓存读
    /// <para>日志源: failedOnly=true → 失败 job 日志; jobId 有值 → 单 job 日志; 否则 → 整个 run 日志</para>
    /// </summary>
    public async Task<ToolResult> StreamAndFilterAsync(
        string owner, string repo, string runId, string? jobId, bool failedOnly,
        string scope, FrozenSet<string>? markers, GitHubLogFilter? filterLevel,
        int maxLines, CancellationToken ct, string? hint = null, int skipLines = 0, bool wantRefresh = false) {
        var matched = new List<string>(maxLines);
        var skipped = 0;
        var lineNumber = 0;

        // 统一日志流入口 — 先入库 LSM 缓存再 yield(架构统一)
        IAsyncEnumerable<string> logLines;
        if (failedOnly || !string.IsNullOrWhiteSpace(jobId) || long.TryParse(runId, out _)) {
            logLines = GetLogStreamAsync(owner, repo, runId, jobId, failedOnly, wantRefresh, ct);
        } else {
            return GitHubToolHandlers.Fail($"无效的 Run ID: {runId}");
        }

        // 优化B: expand=failed 无 filter 时智能定位错误行,不从 runner setup 从头开始
        // 优化C: filter=error 也走智能定位(首屏即见真正错误行),但无错误行时不回退 tail(返回空→"未匹配到任何日志行")
        // 优化E: 指定 jobId + 无 filter 时也走智能定位(expand=failed 按 job 分组,避免从头输出 setup 行)
        var smartStartNoFilter = markers is null && (failedOnly || !string.IsNullOrWhiteSpace(jobId));
        var smartStartWithErrorFilter = markers is not null && markers.Contains(CiLogAnalyzer.ErrorMarker);
        if (smartStartNoFilter || smartStartWithErrorFilter) {
            var fallbackOnNoError = smartStartNoFilter;
            matched = await CollectWithSmartStartAsync(logLines, maxLines, skipLines, fallbackOnNoError, ct).ConfigureAwait(false);
        } else {
            await foreach (var line in logLines.ConfigureAwait(false)) {
                lineNumber++;
                if (markers is not null && !LineMatchesAnyMarker(line.AsSpan(), markers))
                    continue;
                // 先跳过 skipLines 行(分页续读)
                if (skipped < skipLines) { skipped++; continue; }
                // 加行号前缀,方便定位(去时间戳减少噪音)
                matched.Add(LineNumberFormatter.Format(lineNumber, GitHubRunLogText.StripLogTimestamp(line), _compactLinePrefix));
                if (matched.Count >= maxLines) break;
            }
        }
        var prefix = GitHubRunLogFilter.BuildPrefix(runId, scope, filterLevel, matched.Count);
        if (matched.Count == 0) {
            var msg = skipLines > 0
                ? $"未匹配到更多日志行(已跳过 {skipLines} 行)"
                : BuildZeroMatchHint(failedOnly, scope, filterLevel, markers);
            return GitHubToolHandlers.Ok(msg, prefix);
        }
        var text = string.Join('\n', matched);
        // 达到 maxLines 说明可能还有更多行,追加续读提示
        if (matched.Count >= maxLines)
            text += $"\n... [显示第 {skipLines + 1}-{skipLines + matched.Count} 行，用 start_line={skipLines + maxLines + 1} 续读]";
        if (hint is not null) text += hint;
        if (GitHubRunLogFilter.HasNoStackTrace(text))
            text += GitHubRunLogHints.NoStackTraceHint;
        // 优化D: 退出码矛盾检测 — Test Run Successful 但 exit code 1,提示 dotnet test slnx --filter 无匹配测试
        if (text.Contains("Test Run Successful", StringComparison.OrdinalIgnoreCase)
            && text.Contains("exit code 1", StringComparison.OrdinalIgnoreCase))
            text += "\n\n⚠️ 检测到退出码矛盾: 测试全部通过但退出码非0\n可能原因: dotnet test slnx --filter 中某些项目无匹配测试\n建议修复: 添加 -- RunConfiguration.TreatNoTestsAsSuccess=true 或改用直接指定 csproj";
        // 优化F: --log --filter error 使用时提示更高效的 --log_failed(防止 AI 重复犯错)
        if (!failedOnly && markers is not null && markers.Contains(CiLogAnalyzer.ErrorMarker))
            text += "\n\n💡 下次排障建议用 --log_failed 或 --expand failed 更高效(智能定位错误行,首屏即见根因)";
        return GitHubToolHandlers.Ok(text, prefix);
    }

    /// <summary>
    /// 优化B: expand=failed 智能定位错误行 — 滑动窗口扫描,找到首个错误行后输出上下文+后续行
    /// <para>避免从 runner setup 从头输出,AI 首屏即可看到错误降 token</para>
    /// <para>滑动窗口: contextBefore=5 行上下文,未找到错误时按 fallbackOnNoError 决定是否回退到 tail</para>
    /// <para>fallbackOnNoError=true(--log_failed): 无错误行时回退到最后 tailFallback=20 行(至少看到尾部)</para>
    /// <para>fallbackOnNoError=false(filter=error): 收集所有错误行+各自上下文,跳过无关行;无错误行时返回空</para>
    /// </summary>
    private async Task<List<string>> CollectWithSmartStartAsync(
        IAsyncEnumerable<string> logLines, int maxLines, int skipLines, bool fallbackOnNoError, CancellationToken ct)
        => fallbackOnNoError
            ? await CollectSmartStartTailAsync(logLines, maxLines, skipLines, ct).ConfigureAwait(false)
            : await CollectAllErrorContextsAsync(logLines, maxLines, skipLines, ct).ConfigureAwait(false);

    /// <summary>--log_failed 模式：找到第一个错误行,输出上下文 + 后续所有行;无错误时回退 tail</summary>
    private async Task<List<string>> CollectSmartStartTailAsync(
        IAsyncEnumerable<string> logLines, int maxLines, int skipLines, CancellationToken ct) {
        const int contextBefore = 5;
        const int tailFallback = 20;
        var matched = new List<string>(maxLines);
        var contextWindow = new Queue<string>(contextBefore);
        var tailWindow = new Queue<string>(tailFallback);
        var skipped = 0;
        var lineNumber = 0;
        var foundError = false;

        await foreach (var line in logLines.ConfigureAwait(false)) {
            lineNumber++;
            var formatted = LineNumberFormatter.Format(lineNumber, GitHubRunLogText.StripLogTimestamp(line), _compactLinePrefix);
            if (foundError) {
                if (skipped < skipLines) { skipped++; continue; }
                matched.Add(formatted);
                if (matched.Count >= maxLines) break;
            } else if (CiLogAnalyzer.IsErrorIndicator(line.AsSpan())) {
                foundError = true;
                foreach (var ctxLine in contextWindow) {
                    if (skipped < skipLines) { skipped++; continue; }
                    matched.Add(ctxLine);
                    if (matched.Count >= maxLines) break;
                }
                if (matched.Count >= maxLines) break;
                if (skipped < skipLines) { skipped++; continue; }
                matched.Add(formatted);
                if (matched.Count >= maxLines) break;
            } else {
                contextWindow.Enqueue(formatted);
                if (contextWindow.Count > contextBefore) contextWindow.Dequeue();
                tailWindow.Enqueue(formatted);
                if (tailWindow.Count > tailFallback) tailWindow.Dequeue();
            }
        }

        if (!foundError && matched.Count == 0) {
            foreach (var tailLine in tailWindow) {
                if (skipped < skipLines) { skipped++; continue; }
                matched.Add(tailLine);
                if (matched.Count >= maxLines) break;
            }
        }
        return matched;
    }

    /// <summary>filter=error 模式：收集所有错误行 + 各自前5后3上下文,跳过无关 Passed 行(错误前置,信息密度高)</summary>
    /// <para>优化G: 上下文去重 — 多个错误行的上下文窗口重叠时,同一行只输出一次(HashSet 按格式化内容去重)</para>
    /// <para>优化H: 错误摘要前置 — 输出开头先显示错误数+类型分布,AI 一眼看到错误规模(委托 CiLogAnalyzer 统一工具类)</para>
    private async Task<List<string>> CollectAllErrorContextsAsync(
        IAsyncEnumerable<string> logLines, int maxLines, int skipLines, CancellationToken ct) {
        const int contextBefore = 5;
        const int contextAfter = 3;
        var matched = new List<string>(maxLines);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var contextWindow = new Queue<string>(contextBefore);
        var errorCategories = new Dictionary<CiErrorCategory, int>();
        var errorCount = 0;
        var skipped = 0;
        var lineNumber = 0;
        var pendingAfter = 0;

        await foreach (var line in logLines.ConfigureAwait(false)) {
            lineNumber++;
            var formatted = LineNumberFormatter.Format(lineNumber, GitHubRunLogText.StripLogTimestamp(line), _compactLinePrefix);
            if (pendingAfter > 0) {
                if (skipped < skipLines) { skipped++; continue; }
                if (!seen.Add(formatted)) { pendingAfter--; continue; }
                matched.Add(formatted);
                if (matched.Count >= maxLines) break;
                pendingAfter--;
                continue;
            }
            if (!CiLogAnalyzer.IsErrorIndicator(line.AsSpan())) {
                contextWindow.Enqueue(formatted);
                if (contextWindow.Count > contextBefore) contextWindow.Dequeue();
                continue;
            }
            errorCount++;
            var category = CiLogAnalyzer.ClassifyError(line.AsSpan());
            errorCategories[category] = errorCategories.GetValueOrDefault(category) + 1;
            foreach (var ctxLine in contextWindow) {
                if (skipped < skipLines) { skipped++; continue; }
                if (!seen.Add(ctxLine)) continue;
                matched.Add(ctxLine);
                if (matched.Count >= maxLines) break;
            }
            if (matched.Count >= maxLines) break;
            if (skipped < skipLines) { skipped++; continue; }
            if (!seen.Add(formatted)) continue;
            matched.Add(formatted);
            pendingAfter = contextAfter;
            if (matched.Count >= maxLines) break;
        }

        if (errorCount == 0) return matched;
        var result = new List<string>(matched.Count + 1) { CiLogAnalyzer.BuildErrorSummary(errorCount, errorCategories) };
        result.AddRange(matched);
        return result;
    }



    /// <summary>
    /// 构建 0 行匹配的精准提示 — 可能性名单表(每行=可能原因+调查命令),引导 AI 下一步
    /// <para>原则(AGENTS.md): 错误提示必须有诱导方式,禁止纯拒绝无引导</para>
    /// <para>D2: 结构化表格格式,每行编号+可能原因+→调查命令,用户可继续维护此表</para>
    /// </summary>
    internal static string BuildZeroMatchHint(bool failedOnly, string scope, GitHubLogFilter? filterLevel, FrozenSet<string>? markers) {
        var sb = new StringBuilder($"未匹配到任何{scope}行。");
        sb.Append("\n\n可能原因与调查命令:");
        if (failedOnly) {
            sb.Append("\n  1) 所有步骤都通过(无失败步骤)");
            sb.Append("\n     → gh run view <id> --expand jobs  (确认 job 状态)");
        } else if (markers is not null) {
            sb.Append($"\n  1) filter={filterLevel} 不匹配任何行");
            sb.Append("\n     → gh run view <id> --filter all  (放宽过滤)");
            sb.Append("\n     → gh run view <id> --filter error,warning  (换过滤级别)");
        } else {
            sb.Append("\n  1) 日志为空或无匹配内容");
            sb.Append("\n     → gh run view <id> --log  (查看完整日志)");
        }
        sb.Append("\n  2) 需要查看 job 列表或步骤详情");
        sb.Append("\n     → gh run view <id> --expand jobs  (查看 job 列表)");
        sb.Append("\n     → gh run view <id> --expand steps job_id=N  (查看具体步骤)");
        return sb.ToString();
    }

    /// <summary>
    /// 状态机单行推进 — 根据当前状态与行内容更新状态/当前失败/失败列表(纯计算,无 IO)
    /// </summary>
    internal static void ProcessLogLine(string content, int lineNumber, ref LogParseState state, ref TestFailureInfo? current, List<TestFailureInfo> failures) {
        switch (state) {
            case LogParseState.Normal:
            // 检测测试失败标记: "  Failed xxx [FAIL]" 或 "[xUnit.net] xxx [FAIL]"
            if (content.Contains("[FAIL]", StringComparison.OrdinalIgnoreCase) ||
                content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase)) {
                current = new TestFailureInfo { StartLine = lineNumber, TestLine = content };
                failures.Add(current);
                state = LogParseState.InFailedTest;
            }
            // 检测 ##[error] 行
            else if (content.Contains(CiLogAnalyzer.ErrorMarker, StringComparison.OrdinalIgnoreCase)) {
                current = new TestFailureInfo { StartLine = lineNumber, TestLine = content, IsErrorMarker = true };
                failures.Add(current);
                state = LogParseState.Normal;
            }
            break;

            case LogParseState.InFailedTest:
            if (content.StartsWith("  Error Message:", StringComparison.OrdinalIgnoreCase)) {
                state = LogParseState.InErrorMessage;
            } else if (content.StartsWith("  Stack Trace:", StringComparison.OrdinalIgnoreCase)) {
                state = LogParseState.InStackTrace;
            } else if (content.StartsWith("  Passed ", StringComparison.OrdinalIgnoreCase) ||
                       content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase) ||
                       content.Contains("[PASS]", StringComparison.OrdinalIgnoreCase)) {
                state = LogParseState.Normal;
                current = null;
            }
            break;

            case LogParseState.InErrorMessage:
            if (content.StartsWith("  Stack Trace:", StringComparison.OrdinalIgnoreCase)) {
                state = LogParseState.InStackTrace;
            } else if (content.StartsWith("  Passed ", StringComparison.OrdinalIgnoreCase) ||
                       content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase)) {
                state = LogParseState.Normal;
                current = null;
            } else if (current is not null) {
                current.ErrorMessageLines.Add(content.Trim());
            }
            break;

            case LogParseState.InStackTrace:
            if (current is not null) {
                current.StackTraceLines.Add(content);
            }
            if (content.StartsWith("  Passed ", StringComparison.OrdinalIgnoreCase) ||
                content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("--- End of stack trace", StringComparison.OrdinalIgnoreCase)) {
                state = LogParseState.Normal;
                current = null;
            }
            break;
        }
    }

    /// <summary>
    /// 去重失败测试 — 同一测试名可能被 [xUnit.net] [FAIL] 和 Failed 两次报告,保留 ErrorMessage 更多的(纯计算)
    /// </summary>
    internal static List<TestFailureInfo> DeduplicateFailures(List<TestFailureInfo> failures) {
        var deduped = new List<TestFailureInfo>(failures.Count);
        var testNameIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in failures) {
            if (f.IsErrorMarker) {
                deduped.Add(f);
                continue;
            }
            var testName = TestFailureInfo.ExtractTestName(f.TestLine);
            if (testName is not null && testNameIndex.TryGetValue(testName, out var existingIdx)) {
                // 同名失败已存在,保留 ErrorMessage 更多的
                if (f.ErrorMessageLines.Count > deduped[existingIdx].ErrorMessageLines.Count)
                    deduped[existingIdx] = f;
            } else {
                testNameIndex[testName ?? $"__line_{f.StartLine}"] = deduped.Count;
                deduped.Add(f);
            }
        }
        return deduped;
    }

    /// <summary>
    /// Rust 风格输出 — 分页跳过 + 截断 + 续读提示(纯计算,要求 failures 非空)
    /// </summary>
    internal static ToolResult FormatFailuresRustStyle(List<TestFailureInfo> failures, int maxLines, int skipLines, string runId) {
        var sb = new StringBuilder();
        var shown = 0;
        foreach (var f in failures.Skip(skipLines)) {
            if (shown >= maxLines) break;
            shown++;
            sb.Append(f.FormatRustStyle());
            sb.Append('\n');
        }

        var prefix = $"Run {runId} 测试失败({failures.Count} 个,显示 {shown} 个)";
        if (skipLines > 0) prefix += $",跳过前 {skipLines} 个";
        if (skipLines + shown < failures.Count)
            sb.Append($"\n... [共 {failures.Count} 个失败,显示第 {skipLines + 1}-{skipLines + shown} 个,用 start_line={skipLines + shown + 1} 续读]");
        return GitHubToolHandlers.Ok(sb.ToString(), prefix);
    }

    /// <summary>
    /// 日志解析状态机状态
    /// </summary>
    internal enum LogParseState {
        Normal,         // 普通行
        InFailedTest,   // 遇到 Failed/[FAIL],等待 Error Message 或 Stack Trace
        InErrorMessage, // 在 Error Message: 之后
        InStackTrace,   // 在 Stack Trace: 之后
    }

    /// <summary>
    /// 测试失败信息 — 用于 Rust 风格输出
    /// </summary>
    internal sealed class TestFailureInfo {
        public int StartLine;
        public string TestLine = "";
        public List<string> ErrorMessageLines = [];
        public List<string> StackTraceLines = [];
        public bool IsErrorMarker;

        /// <summary>
        /// Rust 风格格式化 — --> line N 指示, | 管道符标注日志行, = 总结行
        /// </summary>
        public string FormatRustStyle() {
            var sb = new StringBuilder();
            sb.Append($"--> line {StartLine}");
            sb.Append('\n');
            sb.Append("   |");
            sb.Append('\n');
            // ##[error] 行: 去掉 ##[error] 前缀,只保留实际错误信息
            var displayLine = TestLine.Trim();
            if (IsErrorMarker && displayLine.StartsWith(CiLogAnalyzer.ErrorMarker, StringComparison.OrdinalIgnoreCase))
                displayLine = displayLine[CiLogAnalyzer.ErrorMarker.Length..].Trim();
            sb.Append($"   | {displayLine}");
            sb.Append('\n');
            if (ErrorMessageLines.Count > 0) {
                sb.Append("   |   Error Message:");
                sb.Append('\n');
                foreach (var em in ErrorMessageLines) {
                    sb.Append($"   |     {em}");
                    sb.Append('\n');
                }
            }
            if (StackTraceLines.Count > 0) {
                sb.Append("   |   Stack Trace:");
                sb.Append('\n');
                foreach (var st in StackTraceLines.Take(10)) {
                    sb.Append($"   | {st.Trim()}");
                    sb.Append('\n');
                }
                if (StackTraceLines.Count > 10)
                    sb.Append($"   | ... ({StackTraceLines.Count - 10} 行未显示)");
            }
            sb.Append("   |");
            sb.Append('\n');
            // 总结行
            if (!IsErrorMarker && ErrorMessageLines.Count > 0) {
                var testName = ExtractTestName(TestLine);
                if (testName is not null)
                    sb.Append($"   = test: {testName}");
                else
                    sb.Append("   = (见上方日志行)");
                sb.Append('\n');
                sb.Append($"   = reason: {ErrorMessageLines[0]}");
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>从测试失败行中提取测试名称。</summary>
        /// <param name="line">测试失败日志行</param>
        /// <returns>测试名称，提取失败时返回 null</returns>
        public static string? ExtractTestName(string line) {
            // 先去掉时间戳前缀 "2026-09-07T17:09:52.2828405Z content"
            var content = GitHubRunLogText.StripLogTimestamp(line).TrimStart();
            // "  Failed Mcp.Tests.xxx [24 ms]" → "Mcp.Tests.xxx"
            // "[xUnit.net 00:00:00.81]     Mcp.Tests.xxx [FAIL]" → "Mcp.Tests.xxx"
            if (content.StartsWith("Failed ", StringComparison.OrdinalIgnoreCase))
                content = content[7..];
            if (content.StartsWith("[xUnit.net", StringComparison.OrdinalIgnoreCase)) {
                var bracketEnd = content.IndexOf(']');
                if (bracketEnd > 0) content = content[(bracketEnd + 1)..].TrimStart();
            }
            // 截取到 [ 之前
            var bracketIdx = content.IndexOf('[');
            if (bracketIdx > 0) content = content[..bracketIdx].Trim();
            return string.IsNullOrEmpty(content) ? null : content;
        }
    }
}