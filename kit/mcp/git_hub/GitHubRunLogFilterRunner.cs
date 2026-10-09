namespace McpToolDispatch;

/// <summary>
/// GitHub Run 日志过滤运行器 — DI 注入 _apiClient/_kvStore,负责失败测试过滤+流式过滤+失败 job 日志获取
/// <para>日志缓存: 复用 IKvStore(LSM-Tree PithosKvStore),首次下载→后续命中缓存,避免重复下载</para>
/// </summary>
internal sealed class GitHubRunLogFilterRunner {
    private readonly IGitHubApiClient _apiClient;
    private readonly IKvStore? _kvStore;

    /// <summary>缓存 TTL — 7 天后自动过期,由 KvStoreTtlCleanupService 定期清理</summary>
    internal static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    /// <summary>
    /// 构造日志过滤运行器,注入 GitHub API 客户端(非 null)+可选 KV 缓存(LSM-Tree)
    /// </summary>
    public GitHubRunLogFilterRunner(IGitHubApiClient apiClient, IKvStore? kvStore = null) {
        _apiClient = apiClient;
        _kvStore = kvStore;
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
        string owner, string repo, string runId, string? jobId, bool failedOnly, bool wantRefresh, CancellationToken ct) {
        if (failedOnly)
            return GetFailedJobLogsAsync(owner, repo, runId, wantRefresh, ct);
        if (!string.IsNullOrWhiteSpace(jobId)) {
            var jobIds = GitHubRunLogFilter.ParseJobIds(jobId);
            return DownloadJobsParallelAsync(owner, repo, runId, jobIds, wantRefresh, ct);
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
        [EnumeratorCancellation] CancellationToken ct) {
        var failedJobIds = await GetOrFetchFailedJobIdsAsync(owner, repo, runId, wantRefresh, ct).ConfigureAwait(false);
        if (failedJobIds.Count == 0) yield break;

        await foreach (var line in DownloadJobsParallelAsync(owner, repo, runId, failedJobIds, wantRefresh, ct).ConfigureAwait(false)) {
            yield return line;
        }
    }

    /// <summary>
    /// 获取失败 job ID 列表 — 优先命中 LSM 缓存(key=gh:jobs:{runId}),未命中则调 API 并缓存
    /// <para>已完成 run 的 job 列表不可变,可安全缓存;wantRefresh=true 跳过缓存读(仍写缓存)</para>
    /// </summary>
    private async Task<List<long>> GetOrFetchFailedJobIdsAsync(string owner, string repo, string runId, bool wantRefresh, CancellationToken ct) {
        var jobsCacheKey = Encoding.UTF8.GetBytes($"gh:jobs:{runId}");
        var timer = GhTimingTracker.CurrentTimer.Value;

        if (!wantRefresh && _kvStore is not null) {
            var tRead = Stopwatch.GetTimestamp();
            var cached = await _kvStore.GetWithTtlAndRenewAsync(jobsCacheKey, CacheTtl, ct).ConfigureAwait(false);
            timer?.AddLsmRead(Stopwatch.GetTimestamp() - tRead);
            if (cached is not null) {
                timer?.RecordHit();
                return ParseCachedJobIds(cached);
            }
        }
        timer?.RecordMiss();

        var tNet = Stopwatch.GetTimestamp();
        var jobsResult = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}/jobs", paginate: true, ct: ct).ConfigureAwait(false);
        timer?.AddNetwork(Stopwatch.GetTimestamp() - tNet);
        if (!jobsResult.Success) return [];

        List<long> failedJobIds;
        try {
            var tParse = Stopwatch.GetTimestamp();
            var jobsResp = JsonSerializer.Deserialize(jobsResult.Body, GitHubApiJsonContext.Safe.RunJobListResponse);
            if (jobsResp?.Jobs is null) return [];
            failedJobIds = [];
            foreach (var job in jobsResp.Jobs) {
                if (job.Id == 0) continue;
                if (string.Equals(job.Conclusion, "failure", StringComparison.OrdinalIgnoreCase))
                    failedJobIds.Add(job.Id);
            }
            timer?.AddParse(Stopwatch.GetTimestamp() - tParse);
        } catch {
            return [];
        }

        if (_kvStore is not null && failedJobIds.Count > 0) {
            var text = string.Join(',', failedJobIds);
            var bytes = Encoding.UTF8.GetBytes(text);
            var tWrite = Stopwatch.GetTimestamp();
            await _kvStore.PutWithTtlAsync(jobsCacheKey, bytes, CacheTtl, ct).ConfigureAwait(false);
            timer?.AddLsmWrite(Stopwatch.GetTimestamp() - tWrite);
        }

        return failedJobIds;
    }

    /// <summary>解析缓存的 job ID 列表(逗号分隔)</summary>
    private static List<long> ParseCachedJobIds(byte[] cached) {
        var text = Encoding.UTF8.GetString(cached);
        var result = new List<long>();
        foreach (var part in text.Split(',')) {
            if (long.TryParse(part, out var id)) result.Add(id);
        }
        return result;
    }

    /// <summary>
    /// 统一多 job 日志下载 — 单 job 走缓存,多 job Channel 并行合并(Actor 邮箱模型)
    /// <para>并行时各 job 行交错合并到 Channel,总时间 ≈ max(各 job) 而非 sum</para>
    /// <para>AGENTS.md 死锁处理规范: Actor 邮箱模型(消息传递替代共享锁)</para>
    /// <para>日志缓存: 复用 IKvStore(LSM-Tree),key=gh:log:{runId}:{jobId},首次下载→后续命中</para>
    /// <para>wantRefresh=true 时跳过缓存读(仍写缓存),用于 rerun 后避免脏数据</para>
    /// </summary>
    public async IAsyncEnumerable<string> DownloadJobsParallelAsync(
        string owner, string repo, string runId, IReadOnlyList<long> jobIds, bool wantRefresh,
        [EnumeratorCancellation] CancellationToken ct) {
        if (jobIds.Count == 0) yield break;

        // 单 job: 走缓存(避免 Channel 开销)
        if (jobIds.Count == 1) {
            await foreach (var line in GetOrFetchJobLogsAsync(owner, repo, runId, jobIds[0], wantRefresh, ct).ConfigureAwait(false)) {
                yield return line;
            }
            yield break;
        }

        // 多 job: Channel 并行合并(Actor 邮箱模型),每个 job 走缓存
        // 并发限制 8,避免 GitHub API 二级限速(对齐旧系统 TryDownloadJobsAsync)
        var channel = Channel.CreateUnbounded<string>();
        var writer = channel.Writer;
        using var semaphore = new SemaphoreSlim(8);

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
    /// <para>key=gh:log:{runId}:{jobId},value=日志全文(UTF-8),首次下载→后续命中避免重复下载</para>
    /// <para>缓存命中时按行分割逐行 yield;未命中时先下载到 List 再写缓存再 yield(保证缓存完整写入)</para>
    /// <para>wantRefresh=true 时跳过缓存读(仍写缓存),用于 rerun 后避免脏数据</para>
    /// </summary>
    public async IAsyncEnumerable<string> GetOrFetchJobLogsAsync(
        string owner, string repo, string runId, long jobId, bool wantRefresh,
        [EnumeratorCancellation] CancellationToken ct) {
        var cacheKey = Encoding.UTF8.GetBytes($"gh:log:{runId}:{jobId}");
        var timer = GhTimingTracker.CurrentTimer.Value;

        // 1. 查 LSM 缓存(wantRefresh 时跳过)
        if (!wantRefresh && _kvStore is not null) {
            var t0 = Stopwatch.GetTimestamp();
            var cached = await _kvStore.GetWithTtlAndRenewAsync(cacheKey, CacheTtl, ct).ConfigureAwait(false);
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
        var lines = new List<string>();
        var tNet = Stopwatch.GetTimestamp();
        await foreach (var line in _apiClient.GetJobLogsAsync(owner, repo, jobId, ct).ConfigureAwait(false)) {
            lines.Add(line);
        }
        timer?.AddNetwork(Stopwatch.GetTimestamp() - tNet);

        // 3. 写入 LSM 缓存
        if (_kvStore is not null && lines.Count > 0) {
            var text = string.Join('\n', lines);
            var bytes = Encoding.UTF8.GetBytes(text);
            var tWrite = Stopwatch.GetTimestamp();
            await _kvStore.PutWithTtlAsync(cacheKey, bytes, CacheTtl, ct).ConfigureAwait(false);
            timer?.AddLsmWrite(Stopwatch.GetTimestamp() - tWrite);
        }

        // 4. 逐行 yield
        foreach (var line in lines) {
            yield return line;
        }
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
        if (!wantRefresh && _kvStore is not null) {
            var t0 = Stopwatch.GetTimestamp();
            var cached = await _kvStore.GetWithTtlAndRenewAsync(cacheKey, CacheTtl, ct).ConfigureAwait(false);
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
        var lines = new List<string>();
        var tNet = Stopwatch.GetTimestamp();
        await foreach (var line in _apiClient.GetRunLogsAsync(owner, repo, long.Parse(runId), ct).ConfigureAwait(false)) {
            lines.Add(line);
        }
        timer?.AddNetwork(Stopwatch.GetTimestamp() - tNet);

        // 3. 写入 LSM 缓存
        if (_kvStore is not null && lines.Count > 0) {
            var text = string.Join('\n', lines);
            var bytes = Encoding.UTF8.GetBytes(text);
            var tWrite = Stopwatch.GetTimestamp();
            await _kvStore.PutWithTtlAsync(cacheKey, bytes, CacheTtl, ct).ConfigureAwait(false);
            timer?.AddLsmWrite(Stopwatch.GetTimestamp() - tWrite);
        }

        // 4. 逐行 yield
        foreach (var line in lines) {
            yield return line;
        }
    }

    /// <summary>解析缓存字节为逐行字符串(跳过空行)</summary>
    private static IEnumerable<string> ParseCachedLines(byte[] cached) {
        var text = Encoding.UTF8.GetString(cached);
        foreach (var line in text.Split('\n')) {
            if (line.Length > 0) yield return line;
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
        return sections.TryGetValue(stepName, out var secs) && secs.TryGetValue(sectionType, out var secLines)
            ? secLines : null;
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
        // 获取日志行枚举源(优先失败 job,其次指定 job,最后整个 run)— 全部走缓存
        IAsyncEnumerable<string> logLines;
        if (string.IsNullOrWhiteSpace(jobId)) {
            logLines = GetFailedJobLogsAsync(owner, repo, runId, wantRefresh, ct);
        } else if (long.TryParse(jobId, out var jobIdLong)) {
            logLines = GetOrFetchJobLogsAsync(owner, repo, runId, jobIdLong, wantRefresh, ct);
        } else {
            logLines = GetOrFetchRunLogsAsync(owner, repo, runId, wantRefresh, ct);
        }

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
        if (failedOnly && markers is null) {
            matched = await CollectWithSmartStartAsync(logLines, maxLines, skipLines, ct).ConfigureAwait(false);
        } else if (markers is not null && markers.Contains("##[error]")) {
            // 缺陷3: filter=error 时自动收集错误行上下文(前5行),避免只有 "Process completed with exit code 1"
            matched = await CollectWithErrorContextAsync(logLines, maxLines, skipLines, ct).ConfigureAwait(false);
        } else {
            await foreach (var line in logLines.ConfigureAwait(false)) {
                lineNumber++;
                if (markers is not null && !LineMatchesAnyMarker(line.AsSpan(), markers))
                    continue;
                // 先跳过 skipLines 行(分页续读)
                if (skipped < skipLines) { skipped++; continue; }
                // 加行号前缀,方便定位(去时间戳减少噪音)
                matched.Add($"{lineNumber}: {GitHubRunLogText.StripLogTimestamp(line)}");
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
            text += $"\n... [可能还有更多行，用 skip_lines={skipLines + maxLines} 续读]";
        if (hint is not null) text += hint;
        if (GitHubRunLogFilter.HasNoStackTrace(text))
            text += GitHubRunLogHints.NoStackTraceHint;
        return GitHubToolHandlers.Ok(text, prefix);
    }

    /// <summary>
    /// 优化B: expand=failed 智能定位错误行 — 滑动窗口扫描,找到首个错误行后输出上下文+后续行
    /// <para>避免从 runner setup 从头输出,AI 首屏即可看到错误降 token</para>
    /// <para>滑动窗口: contextBefore=5 行上下文,未找到错误时回退到最后 tailFallback=20 行</para>
    /// </summary>
    private async Task<List<string>> CollectWithSmartStartAsync(
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
            var formatted = $"{lineNumber}: {GitHubRunLogText.StripLogTimestamp(line)}";
            if (foundError) {
                if (skipped < skipLines) { skipped++; continue; }
                matched.Add(formatted);
                if (matched.Count >= maxLines) break;
            } else if (IsErrorIndicatorLine(line.AsSpan())) {
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

    /// <summary>
    /// 缺陷3: filter=error 时收集错误行+前5行上下文 — 避免 "Process completed with exit code 1" 无具体错误
    /// <para>对每个 ##[error] 行,自动附带前 5 行上下文,让 AI 一次看到具体错误内容</para>
    /// </summary>
    private async Task<List<string>> CollectWithErrorContextAsync(
        IAsyncEnumerable<string> logLines, int maxLines, int skipLines, CancellationToken ct) {
        const int contextBefore = 5;
        const int contextAfter = 3;
        var matched = new List<string>(maxLines);
        var contextWindow = new Queue<string>(contextBefore);
        var skipped = 0;
        var lineNumber = 0;
        var pendingAfter = 0;

        await foreach (var line in logLines.ConfigureAwait(false)) {
            lineNumber++;
            var formatted = $"{lineNumber}: {GitHubRunLogText.StripLogTimestamp(line)}";
            if (pendingAfter > 0) {
                if (skipped < skipLines) { skipped++; continue; }
                matched.Add(formatted);
                pendingAfter--;
                if (matched.Count >= maxLines) break;
                continue;
            }
            if (line.Contains("##[error]", StringComparison.OrdinalIgnoreCase)) {
                if (skipped < skipLines) { skipped++; continue; }
                foreach (var ctxLine in contextWindow) {
                    if (skipped < skipLines) { skipped++; continue; }
                    matched.Add(ctxLine);
                    if (matched.Count >= maxLines) break;
                }
                if (matched.Count >= maxLines) break;
                if (skipped < skipLines) { skipped++; continue; }
                matched.Add(formatted);
                pendingAfter = contextAfter;
                if (matched.Count >= maxLines) break;
            } else {
                contextWindow.Enqueue(formatted);
                if (contextWindow.Count > contextBefore) contextWindow.Dequeue();
            }
        }
        return matched;
    }

    /// <summary>错误指示行检测 — 仅 GitHub Actions 结构化标记 ##[error] 和 Process completed with exit code,避免测试名误匹配</summary>
    private static bool IsErrorIndicatorLine(ReadOnlySpan<char> line)
        => line.Contains("##[error]", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Process completed with exit code", StringComparison.OrdinalIgnoreCase);

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
            else if (content.Contains("##[error]", StringComparison.OrdinalIgnoreCase)) {
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
            sb.Append($"\n... [共 {failures.Count} 个失败,用 skip_lines={skipLines + shown} 续读]");
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
            if (IsErrorMarker && displayLine.StartsWith("##[error]", StringComparison.OrdinalIgnoreCase))
                displayLine = displayLine["##[error]".Length..].Trim();
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