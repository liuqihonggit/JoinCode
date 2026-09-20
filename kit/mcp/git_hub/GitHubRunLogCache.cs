namespace McpToolDispatch;

/// <summary>
/// GitHub Run 日志缓存服务 — DI 注入 _apiClient/_fs/_pipeline/_logger,负责三级缓存( MemoryCache→文件→下载)
/// <para>ADR 0067 两级缓存 + 文件级持久化: 摘要(轻量)+内容(大量行)按 section 独立缓存</para>
/// </summary>
internal sealed class GitHubRunLogCache {
    private readonly IGitHubApiClient _apiClient;
    private readonly IFileSystem _fs;
    private readonly IPersistencePipeline _pipeline;
    private readonly ILogger? _logger;

    /// <summary>
    /// Run 日志缓存 — 用 MemoryCache.Default(系统内存压力自动释放)
    /// <para>两级缓存(ADR 0067): Level1 摘要(轻量)长期保留, Level2 内容(大量行)按 section 独立缓存可被驱逐</para>
    /// <para>24h 过期,内存压力时 Level2 优先被驱逐,Level1 摘要保留,AI 仍可看步骤列表和 section 摘要</para>
    /// </summary>
    private static readonly MemoryCache _logCache = MemoryCache.Default;

    /// <summary>
    /// Level1 摘要缓存 key 前缀 — value=RunLogSummary(步骤名→行数, section类型→行数,轻量)
    /// <para>用 nameof 避免硬编码类名,重构时自动跟随</para>
    /// </summary>
    private static readonly string _summaryPrefix = nameof(GitHubToolHandlers) + ":summary:";

    /// <summary>
    /// Level2 内容缓存 key 前缀 — value=List&lt;string&gt;(section 日志行,大量)
    /// </summary>
    private static readonly string _sectionPrefix = nameof(GitHubToolHandlers) + ":section:";

    /// <summary>
    /// 构造日志缓存服务,DI 注入所有依赖
    /// </summary>
    public GitHubRunLogCache(IGitHubApiClient apiClient, IFileSystem fs, IPersistencePipeline pipeline, ILogger? logger) {
        _apiClient = apiClient;
        _fs = fs;
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <summary>
    /// 从 Level1 摘要缓存获取或流式拉取 — 三级缓存: MemoryCache → 文件级缓存(.jcc/gh_cache/) → 下载
    /// <para>文件级缓存跨进程共享,updatedAt 验证检测 rerun 脏数据,Actor 管道异步写入不阻塞</para>
    /// <para>ADR 0067 两级缓存 + 文件级持久化: 摘要(轻量)+内容(大量行)按 section 独立缓存</para>
    /// </summary>
    public async Task<RunLogSummary?> GetOrFetchSummaryAsync(string owner, string repo, string runId, string? jobId, string? workingDir, bool refresh, CancellationToken ct) {
        var summaryKey = $"{_summaryPrefix}{runId}:{jobId ?? "all"}";

        // 1. MemoryCache 命中
        if (!refresh && _logCache.Get(summaryKey) is RunLogSummary cachedSummary) {
            _logger?.LogDebug("Level1 摘要缓存命中(MemoryCache): {Key}", summaryKey);
            return cachedSummary;
        }

        // 2. 文件级缓存(.jcc/gh_cache/)
        var cacheDir = GitHubRunCachePaths.GetCacheDir(_fs, workingDir);
        var summaryPath = GitHubRunCachePaths.GetCacheFilePath(cacheDir, runId, jobId, "summary.json");
        var rawPath = GitHubRunCachePaths.GetCacheFilePath(cacheDir, runId, jobId, "raw");

        if (!refresh) {
            var fileSummary = await TryGetSummaryFromCacheFileAsync(summaryKey, summaryPath, rawPath, owner, repo, runId, jobId, ct).ConfigureAwait(false);
            if (fileSummary is not null) return fileSummary;
        }

        // 3. 并行下载指定 job(s)(ADR 0067 §10) + 构建 + 缓存
        var summary = new RunLogSummary { RunId = runId, JobId = jobId };
        var sectionContents = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        var rawBuilder = new StringBuilder();

        // 解析 job_id 中的逗号分隔的多个值(如 "123,456")
        var targetJobIds = GitHubRunLogFilter.ParseJobIds(jobId);
        var parallelOk = targetJobIds.Count > 0
            && await TryDownloadJobsAsync(owner, repo, runId, targetJobIds, summary, sectionContents, rawBuilder, ct).ConfigureAwait(false);
        if (!parallelOk) {
            // 回退到串行: 逐个 job 下载(每个 job 独立 GitHubLogParser)
            if (targetJobIds.Count > 0) {
                foreach (var jobIdLong in targetJobIds) {
                    var parser = new GitHubLogParser();
                    await foreach (var line in _apiClient.GetJobLogsAsync(owner, repo, jobIdLong, ct).ConfigureAwait(false)) {
                        rawBuilder.Append(line).Append('\n');
                        parser.ParseLine(line, summary, sectionContents);
                    }
                }
            } else if (long.TryParse(runId, out var runIdLong)) {
                var parser = new GitHubLogParser();
                await foreach (var line in _apiClient.GetRunLogsAsync(owner, repo, runIdLong, ct).ConfigureAwait(false)) {
                    rawBuilder.Append(line).Append('\n');
                    parser.ParseLine(line, summary, sectionContents);
                }
            }
        }

        // 获取 updatedAt 用于后续 rerun 检测
        summary.UpdatedAt = await FetchUpdatedAtAsync(owner, repo, runId, ct).ConfigureAwait(false);

        // 写入 MemoryCache
        foreach (var (stepName, stepSecs) in sectionContents) {
            foreach (var (secType, secLines) in stepSecs) {
                var sectionKey = $"{_sectionPrefix}{runId}:{jobId ?? "all"}:{stepName}:{secType}";
                _logCache.Add(sectionKey, secLines, DateTimeOffset.Now.AddHours(24));
            }
        }
        _logCache.Add(summaryKey, summary, DateTimeOffset.Now.AddHours(24));

        // 通过统一持久化管道异步写文件(fire-and-forget,不阻塞返回) — ADR 0068
        var json = RelaxedJsonSerializer.Serialize(summary, RunLogSummaryJsonContext.Default);
        _pipeline.TryEnqueue(new PersistRequest { Category = "gh_cache", Directory = cacheDir, FileName = Path.GetFileName(summaryPath), Content = json });
        _pipeline.TryEnqueue(new PersistRequest { Category = "gh_cache", Directory = cacheDir, FileName = Path.GetFileName(rawPath), Content = rawBuilder.ToString() });

        _logger?.LogDebug("Level1 摘要已缓存(MemoryCache+文件): {Key}, {Steps} 步骤", summaryKey, summary.StepLineCounts.Count);
        return summary;
    }

    /// <summary>
    /// 从 Level2 内容缓存获取指定 section 的日志行 — MemoryCache → 触发 Level1 填充 → 文件 raw 补填 → 再读
    /// <para>内存压力时 Level2 可被独立驱逐,下次访问时通过 Level1 触发从 .raw 文件重新解析填充</para>
    /// <para>Bug 修复: Level1 MemoryCache 命中时不填充 Level2,需从文件缓存 raw 补填</para>
    /// </summary>
    public async Task<List<string>?> GetOrFetchSectionAsync(
        string owner, string repo, string runId, string? jobId, string stepName, string sectionType,
        string? workingDir, bool refresh, CancellationToken ct) {
        var sectionKey = $"{_sectionPrefix}{runId}:{jobId ?? "all"}:{stepName}:{sectionType}";
        if (!refresh && _logCache.Get(sectionKey) is List<string> cachedLines) {
            _logger?.LogDebug("Level2 内容缓存命中: {Key}, {Lines} 行", sectionKey, cachedLines.Count);
            return cachedLines;
        }

        // Level2 未命中,先确保 Level1 已构建(会从文件或下载填充所有 Level2 缓存)
        await GetOrFetchSummaryAsync(owner, repo, runId, jobId, workingDir, refresh, ct).ConfigureAwait(false);

        // 再次从 Level2 读取
        if (_logCache.Get(sectionKey) is List<string> lines) {
            _logger?.LogDebug("Level2 内容缓存(填充后)命中: {Key}, {Lines} 行", sectionKey, lines.Count);
            return lines;
        }

        // Level2 仍 miss: Level1 MemoryCache 命中但未填充 Level2,从文件缓存 raw 补填
        if (!refresh) {
            var fileLines = TryGetSectionFromFileCache(sectionKey, runId, jobId, workingDir);
            if (fileLines is not null) return fileLines;
        }

        _logger?.LogDebug("Level2 内容缓存未命中(步骤/section 不存在): {Key}", sectionKey);
        return null;
    }

    /// <summary>
    /// 从 GitHub REST API 获取 Run 的 updated_at — 用于检测 rerun 后日志是否更新
    /// <para>轻量 API 调用(不下载日志),&lt; 1s</para>
    /// </summary>
    public async Task<string?> FetchUpdatedAtAsync(string owner, string repo, string runId, CancellationToken ct) {
        var result = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}", ct: ct).ConfigureAwait(false);
        if (!result.Success) return null;
        try {
            using var doc = JsonDocument.Parse(result.Body);
            return doc.RootElement.TryGetProperty("updated_at", out var el) ? el.GetString() : null;
        } catch {
            return null;
        }
    }

    /// <summary>
    /// 从原始日志文本解析并填充 MemoryCache(单进程内 section 内容缓存)
    /// <para>文件级缓存命中时,从 .raw 文件读取解析,避免重新下载</para>
    /// </summary>
    private void FillMemoryCacheFromRaw(string runId, string? jobId, string rawContent) {
        var span = rawContent.AsSpan();
        var ranges = LineSpanIndexer.BuildLineRanges(span);
        var sectionContents = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        var summary = new RunLogSummary { RunId = runId, JobId = jobId };
        var parser = new GitHubLogParser();

        foreach (var (start, length) in ranges) {
            if (length == 0) continue;
            var line = span.Slice(start, length).ToString();
            parser.ParseLine(line, summary, sectionContents);
        }

        foreach (var (stepName, stepSecs) in sectionContents) {
            foreach (var (secType, secLines) in stepSecs) {
                var sectionKey = $"{_sectionPrefix}{runId}:{jobId ?? "all"}:{stepName}:{secType}";
                _logCache.Add(sectionKey, secLines, DateTimeOffset.Now.AddHours(24));
            }
        }
    }

    /// <summary>
    /// 从文件级缓存尝试加载摘要 — MemoryCache 未命中时走 文件→updatedAt验证→填充MemoryCache 链路
    /// <para>返回 null 表示未命中或缓存过期,调用方应重新下载</para>
    /// </summary>
    private async Task<RunLogSummary?> TryGetSummaryFromCacheFileAsync(
        string summaryKey, string summaryPath, string rawPath,
        string owner, string repo, string runId, string? jobId, CancellationToken ct) {
        if (!_fs.FileExists(summaryPath) || !_fs.FileExists(rawPath)) return null;
        var fileAge = DateTimeOffset.Now - _fs.GetLastWriteTime(summaryPath);
        if (fileAge >= TimeSpan.FromHours(24)) return null;
        try {
            var summaryJson = _fs.ReadAllText(summaryPath);
            var fileSummary = RelaxedJsonSerializer.Deserialize(summaryJson, RunLogSummaryJsonContext.Default.RunLogSummary);
            if (fileSummary is null) return null;

            // 5 分钟内跳过 updatedAt 验证(假设 5 分钟内不会 rerun,省 ~1s API 调用)
            if (fileAge < TimeSpan.FromMinutes(5)) {
                var rawContent = _fs.ReadAllText(rawPath);
                FillMemoryCacheFromRaw(runId, jobId, rawContent);
                _logCache.Add(summaryKey, fileSummary, DateTimeOffset.Now.AddHours(24));
                _logger?.LogDebug("Level1 摘要缓存命中(文件,<5min 跳过验证): {Path}, {Steps} 步骤", summaryPath, fileSummary.StepLineCounts.Count);
                return fileSummary;
            }

            // 5 分钟后验证 updatedAt(检测 rerun 脏数据)
            var currentUpdatedAt = await FetchUpdatedAtAsync(owner, repo, runId, ct).ConfigureAwait(false);
            if (currentUpdatedAt is null || fileSummary.UpdatedAt != currentUpdatedAt) {
                // updatedAt 不匹配(CI 已更新),放弃旧缓存(不删除文件,直接重新下载)
                _logger?.LogDebug("updatedAt 不匹配,放弃文件缓存: {Path}", summaryPath);
                return null;
            }

            // 摘要匹配,从 .raw 文件解析填充 MemoryCache
            var raw = _fs.ReadAllText(rawPath);
            FillMemoryCacheFromRaw(runId, jobId, raw);
            _logCache.Add(summaryKey, fileSummary, DateTimeOffset.Now.AddHours(24));
            _logger?.LogDebug("Level1 摘要缓存命中(文件,updatedAt 验证通过): {Path}, {Steps} 步骤", summaryPath, fileSummary.StepLineCounts.Count);
            return fileSummary;
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "文件缓存读取失败,重新下载");
            return null;
        }
    }

    /// <summary>
    /// 从文件缓存 raw 补填 Level2 内容缓存 — Level1 MemoryCache 命中但未填充 Level2 时使用
    /// <para>返回 null 表示补填失败或 section 不存在</para>
    /// </summary>
    private List<string>? TryGetSectionFromFileCache(string sectionKey, string runId, string? jobId, string? workingDir) {
        var cacheDir = GitHubRunCachePaths.GetCacheDir(_fs, workingDir);
        var rawPath = GitHubRunCachePaths.GetCacheFilePath(cacheDir, runId, jobId, "raw");
        if (!_fs.FileExists(rawPath)) return null;
        try {
            var rawContent = _fs.ReadAllText(rawPath);
            FillMemoryCacheFromRaw(runId, jobId, rawContent);
            if (_logCache.Get(sectionKey) is List<string> fileLines) {
                _logger?.LogDebug("Level2 内容缓存(文件 raw 补填)命中: {Key}, {Lines} 行", sectionKey, fileLines.Count);
                return fileLines;
            }
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "文件 raw 补填 Level2 失败: {Path}", rawPath);
        }
        return null;
    }

    /// <summary>
    /// 并行下载指定 job(s) 日志(ADR 0067 §10) — 按需下载,不一次性拉全部 job
    /// <para>失败时返回 false,调用方回退到串行 GetJobLogsAsync</para>
    /// <para>并发度限制 8,避免 GitHub API 二级限速</para>
    /// </summary>
    private async Task<bool> TryDownloadJobsAsync(
        string owner, string repo, string runId, List<long> jobIds,
        RunLogSummary summary,
        Dictionary<string, Dictionary<string, List<string>>> sectionContents,
        StringBuilder rawBuilder, CancellationToken ct) {
        try {
            _logger?.LogDebug("并行下载 {Count} 个 job 日志(按需)", jobIds.Count);

            // 并行下载每个 job 日志(SemaphoreSlim 限并发 8)
            using var semaphore = new SemaphoreSlim(8);
            var tasks = jobIds.Select(async jobId => {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try {
                    for (var attempt = 0; attempt < 3; attempt++) {
                        try {
                            var lines = new List<string>();
                            await foreach (var line in _apiClient.GetJobLogsAsync(owner, repo, jobId, ct).ConfigureAwait(false)) {
                                lines.Add(line);
                            }
                            if (lines.Count > 0)
                                return (jobId, lines);
                            if (attempt < 2)
                                await Task.Delay(500, ct).ConfigureAwait(false);
                        } catch (Exception ex) when (attempt < 2) {
                            _logger?.LogDebug(ex, "job {JobId} 日志下载失败,重试 {Attempt}", jobId, attempt + 1);
                            await Task.Delay(500, ct).ConfigureAwait(false);
                        }
                    }
                    return (jobId, new List<string>());
                } finally { semaphore.Release(); }
            }).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            // 合并日志(每个 job 独立解析,状态机跟踪步骤名)
            foreach (var (_, logLines) in results) {
                if (logLines.Count == 0) continue;
                var parser = new GitHubLogParser();
                foreach (var line in logLines) {
                    if (string.IsNullOrEmpty(line)) continue;
                    rawBuilder.Append(line).Append('\n');
                    parser.ParseLine(line, summary, sectionContents);
                }
            }

            _logger?.LogDebug("并行下载完成, {Steps} 步骤", summary.StepLineCounts.Count);
            return summary.StepLineCounts.Count > 0;
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "并行下载失败,回退到串行");
            return false;
        }
    }

    /// <summary>
    /// GitHub Actions 日志行解析器 — 状态机 + Span,零 GC 逐行处理
    /// <para>跟踪两类步骤: ##[start-action display=...] (action 步骤) 和 ##[group]Run cmd (run 步骤)</para>
    /// <para>action 步骤优先级高于 run 步骤,action 内部的 ##[group]Run 归 action 步骤</para>
    /// <para>Span 处理时间戳剥离和标记检测,只在提取步骤名时 ToString 分配</para>
    /// </summary>
    private sealed class GitHubLogParser {
        private string? _currentStepName;
        private bool _inAction;
        private bool _inRunGroup;
        private static readonly SearchValues<char> s_nameTerminators = SearchValues.Create(";]");

        /// <summary>
        /// 解析一行日志并累积 — 状态机跟踪步骤名,Span 检测标记
        /// </summary>
        public void ParseLine(string line, RunLogSummary summary, Dictionary<string, Dictionary<string, List<string>>> sectionContents) {
            var content = StripTimestamp(line.AsSpan());

            // 优先级1: ##[start-action display=StepName;id=...]
            if (content.StartsWith("##[start-action display=".AsSpan())) {
                var rest = content.Slice("##[start-action display=".Length);
                var endIdx = rest.IndexOfAny(s_nameTerminators);
                _currentStepName = endIdx > 0 ? rest[..endIdx].ToString() : rest.ToString();
                _inAction = true;
                _inRunGroup = false;
                return;
            }

            // 优先级2: ##[end-action
            if (content.StartsWith("##[end-action".AsSpan())) {
                _currentStepName = null;
                _inAction = false;
                _inRunGroup = false;
                return;
            }

            // 优先级3: ##[group]Run cmd (仅当不在 action 中,action 内部的 group 归 action)
            if (!_inAction && content.StartsWith("##[group]Run ".AsSpan())) {
                var cmd = content.Slice("##[group]Run ".Length);
                _currentStepName = ExtractRunStepName(cmd);
                _inRunGroup = true;
                return;
            }

            // 优先级4: ##[endgroup] (仅当在 run group 中)
            // 不清除 _currentStepName — ##[endgroup] 只结束命令回显,实际输出在 endgroup 之后
            // 步骤持续到下一个 ##[start-action] 或 ##[group]Run 切换
            if (_inRunGroup && content.StartsWith("##[endgroup]".AsSpan())) {
                _inRunGroup = false;
                return;
            }

            if (_currentStepName is not null) {
                GitHubRunStepExtractor.Accumulate(line, _currentStepName, summary, sectionContents);
                return;
            }

            // 回退: [entry.Name] 前缀 或 TSV 格式
            var stepName = TryExtractStepName(line.AsSpan());
            if (stepName is not null)
                GitHubRunStepExtractor.Accumulate(line, stepName, summary, sectionContents);
        }

        /// <summary>
        /// 从 ##[group]Run 命令提取简短步骤名 — Span 处理,零 GC
        /// <para>"dotnet test xxx.csproj ..." → "dotnet test xxx"</para>
        /// <para>"dotnet build xxx.csproj ..." → "dotnet build xxx"</para>
        /// <para>"actions/checkout@v5" → "actions/checkout@v5"</para>
        /// <para>"./.github/actions/setup-test-env" → "setup-test-env"</para>
        /// <para>其他 → 截断到 60 字符</para>
        /// </summary>
        private static string ExtractRunStepName(ReadOnlySpan<char> cmd) {
            // dotnet test xxx.csproj ... → dotnet test xxx
            if (cmd.StartsWith("dotnet test ".AsSpan())) {
                var after = cmd.Slice("dotnet test ".Length);
                var csprojIdx = after.IndexOf(".csproj".AsSpan());
                if (csprojIdx > 0) {
                    var path = after[..csprojIdx];
                    var lastSlash = path.LastIndexOf('/');
                    var shortName = lastSlash >= 0 ? path.Slice(lastSlash + 1) : path;
                    return string.Concat("dotnet test ", shortName.ToString());
                }
                return "dotnet test";
            }

            // dotnet build xxx.csproj ... → dotnet build xxx
            if (cmd.StartsWith("dotnet build ".AsSpan())) {
                var after = cmd.Slice("dotnet build ".Length);
                var csprojIdx = after.IndexOf(".csproj".AsSpan());
                if (csprojIdx > 0) {
                    var path = after[..csprojIdx];
                    var lastSlash = path.LastIndexOf('/');
                    var shortName = lastSlash >= 0 ? path.Slice(lastSlash + 1) : path;
                    return string.Concat("dotnet build ", shortName.ToString());
                }
                return "dotnet build";
            }

            // ./.github/actions/xxx → xxx
            if (cmd.StartsWith("./.github/actions/".AsSpan())) {
                var after = cmd.Slice("./.github/actions/".Length);
                var spaceIdx = after.IndexOf(' ');
                var name = spaceIdx > 0 ? after[..spaceIdx] : after;
                return name.ToString();
            }

            // 其他: 截断到 60 字符
            return cmd.Length <= 60 ? cmd.ToString() : cmd[..60].ToString();
        }

        /// <summary>
        /// 剥离时间戳前缀 — "2026-09-07T17:08:27.5016453Z content" → "content",返回 Span 不分配
        /// </summary>
        private static ReadOnlySpan<char> StripTimestamp(ReadOnlySpan<char> span) {
            // 时间戳格式: "2026-09-07T17:08:27.5016453Z content"
            var zIdx = span.IndexOf('Z');
            if (zIdx > 0 && zIdx + 2 < span.Length && span[zIdx + 1] == ' ')
                return span.Slice(zIdx + 2);
            // [entry.Name] content
            if (span.Length > 0 && span[0] == '[') {
                var closeIdx = span.IndexOf(']');
                if (closeIdx > 0 && closeIdx + 2 < span.Length)
                    return span.Slice(closeIdx + 2);
            }
            return span;
        }

        /// <summary>
        /// 从日志行 Span 提取步骤名 — [entry.Name] 前缀优先,回退 TSV,只在找到时 ToString
        /// </summary>
        private static string? TryExtractStepName(ReadOnlySpan<char> span) {
            // [entry.Name] line → GitHubRunStepExtractor.ExtractStepNameFromEntryName(entry.Name)
            if (span.Length > 0 && span[0] == '[') {
                var closeIdx = span.IndexOf(']');
                if (closeIdx > 1)
                    return ExtractStepNameFromEntryName(span[1..closeIdx]);
            }
            // TSV: col1\tstepName\t...
            var tabIdx = span.IndexOf('\t');
            if (tabIdx < 0) return null;
            var remaining = span.Slice(tabIdx + 1);
            var secondTabIdx = remaining.IndexOf('\t');
            return secondTabIdx >= 0 ? remaining[..secondTabIdx].ToString() : remaining.ToString();
        }

        /// <summary>
        /// 从 zip entry 名 Span 提取步骤名 — "0_Checkout.txt" → "Checkout"
        /// </summary>
        private static string ExtractStepNameFromEntryName(ReadOnlySpan<char> entryName) {
            var name = entryName;
            var slashIdx = name.LastIndexOf('/');
            if (slashIdx >= 0) name = name.Slice(slashIdx + 1);
            var dotIdx = name.LastIndexOf('.');
            if (dotIdx > 0) name = name[..dotIdx];
            var underscoreIdx = name.IndexOf('_');
            if (underscoreIdx > 0 && int.TryParse(name[..underscoreIdx], CultureInfo.InvariantCulture, out _))
                name = name.Slice(underscoreIdx + 1);
            return name.Length == 0 ? entryName.ToString() : name.ToString();
        }
    }
}