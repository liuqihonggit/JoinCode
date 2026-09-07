namespace McpToolDispatch;

/// <summary>
/// GitHub Actions Run 工具 — gh run 子命令全套
/// <para>避坑2/3/5: 大日志 maxLines 截断 + --job 精准拉 + 30s 超时</para>
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhRunList, "列出 Actions Run(支持状态/分支过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunListAsync(
        [McpToolParameter("数量限制(默认 20)", Required = false)] int? limit = null,
        [McpToolParameter("状态过滤(queued/in_progress/completed,可选)", Required = false)] string? status = null,
        [McpToolParameter("分支过滤(可选)", Required = false)] string? branch = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 20).ToString() };
        if (!string.IsNullOrWhiteSpace(status)) query["status"] = status;
        if (!string.IsNullOrWhiteSpace(branch)) query["branch"] = branch;

        var result = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/runs", query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);

        var hasFailure = result.Body.Contains("\"conclusion\":\"failure\"", StringComparison.OrdinalIgnoreCase);
        return hasFailure ? Ok(result.Body + RunListFailureHint) : Ok(result.Body);
    }

    [McpTool(GitHubToolNameConstants.GhRunView, "查看 Run 详情/日志(expand 按步骤展开+文件级缓存跨进程,filter 按标记过滤,skip_lines 分页续读,refresh 强制刷新)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunViewAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("Job ID(可选,精准拉单 job)", Required = false)] string? job_id = null,
        [McpToolParameter("是否拉取日志(默认 false,仅看详情)", Required = false)] bool? log = null,
        [McpToolParameter("最大日志行数(默认 200)", Required = false)] int? max_lines = null,
        [McpToolParameter("跳过前 N 行(用于续读截断日志,默认 0)", Required = false)] int? skip_lines = null,
        [McpToolParameter("按步骤展开: steps=列出步骤列表, failed=只拉失败步骤, step:Name=只拉指定步骤(复刻 ToolSearch map[] 逐层drill down)", Required = false)] string? expand = null,
        [McpToolParameter("日志过滤级别(error/warning/info/all,默认 all=不过滤)", Required = false)] string? filter = null,
        [McpToolParameter("强制刷新缓存(默认 false,rerun 后用 true 避免脏数据)", Required = false)] bool? refresh = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var maxLines = max_lines ?? 200;
        var skip = skip_lines ?? 0;
        var wantRefresh = refresh == true;
        // MCP 框架可能把缺失的 string? 参数传成空字符串,统一归一化为 null
        job_id = string.IsNullOrWhiteSpace(job_id) ? null : job_id;
        var hasFilter = TryParseLogFilter(filter, out var filterLevel) && filterLevel != GitHubLogFilter.All;
        var markers = hasFilter ? GetFilterMarkers(filterLevel) : null;

        // === expand=failed: 只拉失败步骤日志(量少,不缓存) ===
        if (string.Equals(expand, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return await StreamAndFilterAsync(owner, repoName, run_id, job_id, true, "失败步骤", markers, filterLevel, maxLines, cancellationToken, FailedHint, skip);
        }

        // === expand=steps 或 expand=step:Name: 两级缓存(ADR 0067) ===
        var expandStep = expand?.StartsWith("step:", StringComparison.OrdinalIgnoreCase) == true
            ? expand[5..].Trim()
            : null;
        var wantSteps = string.Equals(expand, "steps", StringComparison.OrdinalIgnoreCase);

        if (wantSteps || expandStep is not null)
        {
            // 解析 /section:Type 后缀
            string? sectionType = null;
            if (expandStep is not null)
            {
                var sectionIdx = expandStep.IndexOf("/section:", StringComparison.OrdinalIgnoreCase);
                if (sectionIdx >= 0)
                {
                    sectionType = expandStep[(sectionIdx + 9)..].Trim();
                    expandStep = expandStep[..sectionIdx].Trim();
                }
            }

            // expand=step:Name/section:Type: 从 Level2 内容缓存读取(ADR 0067)
            if (expandStep is not null && sectionType is not null)
            {
                var sectionLines = await GetOrFetchSectionAsync(owner, repoName, run_id, job_id, expandStep, sectionType, working_dir, wantRefresh, cancellationToken);
                if (sectionLines is null)
                    return Ok($"未找到步骤 '{expandStep}' 或 section '{sectionType}'，建议先 expand=step:{expandStep} 查看 section 摘要");

                var (secText, secHasMore) = SkipAndTruncate(sectionLines, maxLines, skip);
                if (secHasMore)
                    secText += TruncatedHint;
                if (HasNoStackTrace(secText))
                    secText += NoStackTraceHint;
                var secPrefix = BuildPrefix(run_id, $"步骤:{expandStep}/section:{sectionType}", filterLevel, sectionLines.Count);
                return Ok(secText, secPrefix);
            }

            // 其余情况(expand=steps 或 expand=step:Name): 从 Level1 摘要缓存读取
            var summary = await GetOrFetchSummaryAsync(owner, repoName, run_id, job_id, working_dir, wantRefresh, cancellationToken);
            if (summary is null) return Fail("日志拉取失败");

            // expand=steps: 返回步骤列表
            if (wantSteps)
            {
                var stepsText = summary.StepLineCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Select(kvp => $"  {kvp.Value,6} 行  {kvp.Key}");
                return Ok(string.Join('\n', stepsText) + StepsHint, $"Run {run_id} 步骤列表({summary.StepLineCounts.Count} 步骤,缓存于 {summary.CachedAt:HH:mm:ss}):");
            }

            // expand=step:Name: 返回 section 摘要(Level 2,ADR 0067)
            if (expandStep is not null)
            {
                if (!summary.SectionCounts.TryGetValue(expandStep, out var secs))
                    return Ok($"未找到步骤 '{expandStep}'，可用步骤: {string.Join(", ", summary.SectionCounts.Keys)}");

                var summaryText = secs
                    .OrderBy(kvp => SectionOrder(kvp.Key))
                    .Select(kvp => $"  {kvp.Key,-8} {kvp.Value,5} 行  (用 expand=step:{expandStep}/section:{kvp.Key} 查看)");
                return Ok(string.Join('\n', summaryText) + SectionHint, $"Run {run_id} 步骤:{expandStep} sections({secs.Count} 类):");
            }
        }

        // === 常规模式: log=false 看详情, log=true 拉日志 ===
        var wantLog = log == true;

        if (wantLog)
        {
            // log=true: 用 REST API 日志流 + 过滤/分页
            return await StreamAndFilterAsync(owner, repoName, run_id, job_id, false, "日志", markers, filterLevel, maxLines, cancellationToken, LogHint, skip);
        }

        // log=false: 获取 run 详情 JSON
        var detailResult = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/runs/{run_id}", ct: cancellationToken).ConfigureAwait(false);
        return detailResult.Success ? Ok(detailResult.Body) : Fail(detailResult.Error);
    }

    /// <summary>
    /// 从 Level1 摘要缓存获取或流式拉取 — 三级缓存: MemoryCache → 文件级缓存(.jcc/gh_cache/) → 下载
    /// <para>文件级缓存跨进程共享,updatedAt 验证检测 rerun 脏数据,Actor 管道异步写入不阻塞</para>
    /// <para>ADR 0067 两级缓存 + 文件级持久化: 摘要(轻量)+内容(大量行)按 section 独立缓存</para>
    /// </summary>
    private async Task<RunLogSummary?> GetOrFetchSummaryAsync(string owner, string repo, string runId, string? jobId, string? workingDir, bool refresh, CancellationToken ct)
    {
        var summaryKey = $"{_summaryPrefix}{runId}:{jobId ?? "all"}";

        // 1. MemoryCache 命中
        if (!refresh && _logCache.Get(summaryKey) is RunLogSummary cachedSummary)
        {
            _logger?.LogDebug("Level1 摘要缓存命中(MemoryCache): {Key}", summaryKey);
            return cachedSummary;
        }

        // 2. 文件级缓存(.jcc/gh_cache/)
        var cacheDir = GetCacheDir(workingDir);
        var summaryPath = GetCacheFilePath(cacheDir, runId, jobId, "summary.json");
        var rawPath = GetCacheFilePath(cacheDir, runId, jobId, "raw");

        if (!refresh && _fs.FileExists(summaryPath) && _fs.FileExists(rawPath))
        {
            var fileAge = DateTimeOffset.Now - _fs.GetLastWriteTime(summaryPath);
            if (fileAge < TimeSpan.FromHours(24))
            {
                try
                {
                    var summaryJson = _fs.ReadAllText(summaryPath);
                    var fileSummary = RelaxedJsonSerializer.Deserialize(summaryJson, RunLogSummaryJsonContext.Default.RunLogSummary);
                    if (fileSummary is not null)
                    {
                        // 5 分钟内跳过 updatedAt 验证(假设 5 分钟内不会 rerun,省 ~1s API 调用)
                        var needVerify = fileAge >= TimeSpan.FromMinutes(5);
                        if (!needVerify)
                        {
                            var rawContent = _fs.ReadAllText(rawPath);
                            FillMemoryCacheFromRaw(runId, jobId, rawContent);
                            _logCache.Add(summaryKey, fileSummary, DateTimeOffset.Now.AddHours(24));
                            _logger?.LogDebug("Level1 摘要缓存命中(文件,<5min 跳过验证): {Path}, {Steps} 步骤", summaryPath, fileSummary.StepLineCounts.Count);
                            return fileSummary;
                        }

                        // 5 分钟后验证 updatedAt(检测 rerun 脏数据)
                        var currentUpdatedAt = await FetchUpdatedAtAsync(owner, repo, runId, ct).ConfigureAwait(false);
                        if (currentUpdatedAt is not null && fileSummary.UpdatedAt == currentUpdatedAt)
                        {
                            // 摘要匹配,从 .raw 文件解析填充 MemoryCache
                            var rawContent = _fs.ReadAllText(rawPath);
                            FillMemoryCacheFromRaw(runId, jobId, rawContent);
                            _logCache.Add(summaryKey, fileSummary, DateTimeOffset.Now.AddHours(24));
                            _logger?.LogDebug("Level1 摘要缓存命中(文件,updatedAt 验证通过): {Path}, {Steps} 步骤", summaryPath, fileSummary.StepLineCounts.Count);
                            return fileSummary;
                        }
                        // updatedAt 不匹配(CI 已更新),放弃旧缓存(不删除文件,直接重新下载)
                        _logger?.LogDebug("updatedAt 不匹配,放弃文件缓存: {Path}", summaryPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "文件缓存读取失败,重新下载");
                }
            }
        }

        // 3. 并行下载(ADR 0067 §10) + 构建 + 缓存
        var summary = new RunLogSummary { RunId = runId, JobId = jobId };
        var sectionContents = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        var rawBuilder = new StringBuilder();

        var parallelOk = string.IsNullOrWhiteSpace(jobId)
            && await TryDownloadParallelAsync(owner, repo, runId, summary, sectionContents, rawBuilder, ct).ConfigureAwait(false);
        if (!parallelOk)
        {
            // 回退到串行 REST API 日志流
            if (long.TryParse(runId, out var runIdLong))
            {
                if (!string.IsNullOrWhiteSpace(jobId) && long.TryParse(jobId, out var jobIdLong))
                {
                    await foreach (var line in _apiClient!.GetJobLogsAsync(owner, repo, jobIdLong, ct).ConfigureAwait(false))
                    {
                        rawBuilder.Append(line).Append('\n');
                        ParseAndAccumulate(line, summary, sectionContents);
                    }
                }
                else
                {
                    await foreach (var line in _apiClient!.GetRunLogsAsync(owner, repo, runIdLong, ct).ConfigureAwait(false))
                    {
                        rawBuilder.Append(line).Append('\n');
                        ParseAndAccumulate(line, summary, sectionContents);
                    }
                }
            }
        }

        // 获取 updatedAt 用于后续 rerun 检测
        summary.UpdatedAt = await FetchUpdatedAtAsync(owner, repo, runId, ct).ConfigureAwait(false);

        // 写入 MemoryCache
        foreach (var (stepName, stepSecs) in sectionContents)
        {
            foreach (var (secType, secLines) in stepSecs)
            {
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
    /// 从 Level2 内容缓存获取指定 section 的日志行 — MemoryCache → 触发 Level1 填充 → 再读
    /// <para>内存压力时 Level2 可被独立驱逐,下次访问时通过 Level1 触发从 .raw 文件重新解析填充</para>
    /// </summary>
    private async Task<List<string>?> GetOrFetchSectionAsync(
        string owner, string repo, string runId, string? jobId, string stepName, string sectionType,
        string? workingDir, bool refresh, CancellationToken ct)
    {
        var sectionKey = $"{_sectionPrefix}{runId}:{jobId ?? "all"}:{stepName}:{sectionType}";
        if (!refresh && _logCache.Get(sectionKey) is List<string> cachedLines)
        {
            _logger?.LogDebug("Level2 内容缓存命中: {Key}, {Lines} 行", sectionKey, cachedLines.Count);
            return cachedLines;
        }

        // Level2 未命中,先确保 Level1 已构建(会从文件或下载填充所有 Level2 缓存)
        await GetOrFetchSummaryAsync(owner, repo, runId, jobId, workingDir, refresh, ct).ConfigureAwait(false);

        // 再次从 Level2 读取
        if (_logCache.Get(sectionKey) is List<string> lines)
        {
            _logger?.LogDebug("Level2 内容缓存(填充后)命中: {Key}, {Lines} 行", sectionKey, lines.Count);
            return lines;
        }

        _logger?.LogDebug("Level2 内容缓存未命中(步骤/section 不存在): {Key}", sectionKey);
        return null;
    }

    /// <summary>
    /// 从 GitHub REST API 获取 Run 的 updated_at — 用于检测 rerun 后日志是否更新
    /// <para>轻量 API 调用(不下载日志),&lt; 1s</para>
    /// </summary>
    private async Task<string?> FetchUpdatedAtAsync(string owner, string repo, string runId, CancellationToken ct)
    {
        var result = await _apiClient!.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}", ct: ct).ConfigureAwait(false);
        if (!result.Success) return null;
        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            return doc.RootElement.TryGetProperty("updated_at", out var el) ? el.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从原始日志文本解析并填充 MemoryCache(单进程内 section 内容缓存)
    /// <para>文件级缓存命中时,从 .raw 文件读取解析,避免重新下载</para>
    /// </summary>
    private void FillMemoryCacheFromRaw(string runId, string? jobId, string rawContent)
    {
        var span = rawContent.AsSpan();
        var ranges = LineSpanIndexer.BuildLineRanges(span);
        var sectionContents = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (start, length) in ranges)
        {
            if (length == 0) continue;
            var line = span.Slice(start, length).ToString();
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var stepName = parts[1];
            var sectionType = RunLogCache.ParseSectionType(line);

            if (!sectionContents.TryGetValue(stepName, out var stepSecs))
            {
                stepSecs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                sectionContents[stepName] = stepSecs;
            }
            if (!stepSecs.TryGetValue(sectionType, out var secLines))
            {
                secLines = new List<string>();
                stepSecs[sectionType] = secLines;
            }
            secLines.Add(line);
        }

        foreach (var (stepName, stepSecs) in sectionContents)
        {
            foreach (var (secType, secLines) in stepSecs)
            {
                var sectionKey = $"{_sectionPrefix}{runId}:{jobId ?? "all"}:{stepName}:{secType}";
                _logCache.Add(sectionKey, secLines, DateTimeOffset.Now.AddHours(24));
            }
        }
    }

    /// <summary>
    /// 按 job 并行下载日志(ADR 0067 §10) — 25 个 job 并行,理论 10-15s vs 串行 43s
    /// <para>失败时返回 false,调用方回退到串行 GetRunLogsAsync</para>
    /// <para>并发度限制 8,避免 GitHub API 二级限速</para>
    /// </summary>
    private async Task<bool> TryDownloadParallelAsync(
        string owner, string repo, string runId,
        RunLogSummary summary,
        Dictionary<string, Dictionary<string, List<string>>> sectionContents,
        StringBuilder rawBuilder, CancellationToken ct)
    {
        try
        {
            // 1. 获取 job 列表(owner/repo 已传入)
            var jobsResult = await _apiClient!.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}/jobs", paginate: true, ct: ct).ConfigureAwait(false);
            if (!jobsResult.Success) return false;

            List<(long id, string name)> jobs;
            using (var doc = JsonDocument.Parse(jobsResult.Body))
            {
                if (!doc.RootElement.TryGetProperty("jobs", out var jobsEl)) return false;
                jobs = new List<(long, string)>();
                foreach (var job in jobsEl.EnumerateArray())
                {
                    if (!job.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;
                    var name = job.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "unknown" : "unknown";
                    jobs.Add((idEl.GetInt64(), name));
                }
            }
            if (jobs.Count == 0) return false;

            _logger?.LogDebug("并行下载 {Count} 个 job 日志", jobs.Count);

            // 2. 并行下载每个 job 日志(SemaphoreSlim 限并发 8)
            using var semaphore = new SemaphoreSlim(8);
            var tasks = jobs.Select(async job =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            var lines = new List<string>();
                            await foreach (var line in _apiClient.GetJobLogsAsync(owner, repo, job.id, ct).ConfigureAwait(false))
                            {
                                lines.Add(line);
                            }
                            if (lines.Count > 0)
                                return (job.name, lines);
                            if (attempt < 2)
                                await Task.Delay(500, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (attempt < 2)
                        {
                            _logger?.LogDebug(ex, "job {JobId} 日志下载失败,重试 {Attempt}", job.id, attempt + 1);
                            await Task.Delay(500, ct).ConfigureAwait(false);
                        }
                    }
                    return (job.name, new List<string>());
                }
                finally { semaphore.Release(); }
            }).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            // 3. 合并日志(统一用 ParseAndAccumulate 从 [entry.Name] 提取 step 名)
            foreach (var (_, logLines) in results)
            {
                if (logLines.Count == 0) continue;
                foreach (var line in logLines)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    rawBuilder.Append(line).Append('\n');
                    ParseAndAccumulate(line, summary, sectionContents);
                }
            }

            _logger?.LogDebug("并行下载完成, {Steps} 步骤", summary.StepLineCounts.Count);
            return summary.StepLineCounts.Count > 0;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "并行下载失败,回退到串行");
            return false;
        }
    }

    /// <summary>
    /// 解析一行日志并累积 — 支持 REST API 格式 [entry.Name] line 和 gh CLI TSV 格式
    /// <para>REST API: zip entry 名如 "0_Checkout.txt" → step 名 "Checkout"</para>
    /// <para>gh CLI: TSV 第二列是 step 名</para>
    /// </summary>
    private static void ParseAndAccumulate(string line, RunLogSummary summary, Dictionary<string, Dictionary<string, List<string>>> sectionContents)
    {
        var stepName = TryExtractStepName(line);
        if (stepName is null) return;
        Accumulate(line, stepName, summary, sectionContents);
    }

    /// <summary>
    /// 从日志行提取步骤名 — REST API 格式 [entry.Name] line 优先,回退 gh CLI TSV 格式
    /// </summary>
    private static string? TryExtractStepName(string line)
    {
        // REST API 格式: [entry.Name] logLine — entry.Name 是 zip 文件名(如 0_Checkout.txt)
        if (line.StartsWith('['))
        {
            var closeIdx = line.IndexOf(']');
            if (closeIdx > 1)
            {
                return ExtractStepNameFromEntryName(line[1..closeIdx]);
            }
        }

        // gh CLI TSV 格式: 列1\t步骤名\t...
        var tabIdx = line.IndexOf('\t');
        if (tabIdx < 0) return null;
        var secondTabIdx = line.IndexOf('\t', tabIdx + 1);
        return secondTabIdx > tabIdx ? line[(tabIdx + 1)..secondTabIdx] : line[(tabIdx + 1)..];
    }

    /// <summary>
    /// 从 zip entry 名提取步骤名 — "0_Checkout.txt" → "Checkout", "Build.txt" → "Build", "0_build/1_Test.txt" → "Test"
    /// </summary>
    private static string ExtractStepNameFromEntryName(string entryName)
    {
        var name = entryName;
        var slashIdx = name.LastIndexOf('/');
        if (slashIdx >= 0) name = name[(slashIdx + 1)..];
        var dotIdx = name.LastIndexOf('.');
        if (dotIdx > 0) name = name[..dotIdx];
        var underscoreIdx = name.IndexOf('_');
        if (underscoreIdx > 0 && int.TryParse(name.AsSpan(0, underscoreIdx), out _))
            name = name[(underscoreIdx + 1)..];
        return name.Length == 0 ? entryName : name;
    }

    /// <summary>
    /// 累积一行日志到 summary 和 sectionContents — 用指定 stepName
    /// </summary>
    private static void Accumulate(string line, string stepName, RunLogSummary summary, Dictionary<string, Dictionary<string, List<string>>> sectionContents)
    {
        var sectionType = RunLogCache.ParseSectionType(line);

        summary.StepLineCounts[stepName] = summary.StepLineCounts.GetValueOrDefault(stepName) + 1;

        if (!summary.SectionCounts.TryGetValue(stepName, out var secCounts))
        {
            secCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            summary.SectionCounts[stepName] = secCounts;
        }
        secCounts[sectionType] = secCounts.GetValueOrDefault(sectionType) + 1;

        if (!sectionContents.TryGetValue(stepName, out var stepSecs))
        {
            stepSecs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            sectionContents[stepName] = stepSecs;
        }
        if (!stepSecs.TryGetValue(sectionType, out var secLines))
        {
            secLines = new List<string>();
            stepSecs[sectionType] = secLines;
        }
        secLines.Add(line);
    }

    /// <summary>
    /// 获取指定 Run 中所有失败 job 的日志 — 逐行 yield(合并多个 job 日志)
    /// <para>用于 expand=failed 模式,只拉 conclusion=failure 的 job 日志</para>
    /// </summary>
    private async IAsyncEnumerable<string> GetFailedJobLogsAsync(
        string owner, string repo, string runId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var jobsResult = await _apiClient!.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}/jobs", paginate: true, ct: ct).ConfigureAwait(false);
        if (!jobsResult.Success) yield break;

        List<long> failedJobIds;
        using (var doc = JsonDocument.Parse(jobsResult.Body))
        {
            if (!doc.RootElement.TryGetProperty("jobs", out var jobsEl)) yield break;
            failedJobIds = new List<long>();
            foreach (var job in jobsEl.EnumerateArray())
            {
                if (!job.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;
                var conclusion = job.TryGetProperty("conclusion", out var conEl) ? conEl.GetString() : null;
                if (string.Equals(conclusion, "failure", StringComparison.OrdinalIgnoreCase))
                    failedJobIds.Add(idEl.GetInt64());
            }
        }

        foreach (var jobId in failedJobIds)
        {
            await foreach (var line in _apiClient.GetJobLogsAsync(owner, repo, jobId, ct).ConfigureAwait(false))
            {
                yield return line;
            }
        }
    }

    /// <summary>
    /// 流式拉取 + 过滤 + 分页跳过(不缓存,用于 --log-failed 或一次性过滤)
    /// <para>日志源: failedOnly=true → 失败 job 日志; jobId 有值 → 单 job 日志; 否则 → 整个 run 日志</para>
    /// </summary>
    private async Task<ToolResult> StreamAndFilterAsync(
        string owner, string repo, string runId, string? jobId, bool failedOnly,
        string scope, FrozenSet<string>? markers, GitHubLogFilter? filterLevel,
        int maxLines, CancellationToken ct, string? hint = null, int skipLines = 0)
    {
        var matched = new List<string>(maxLines);
        var skipped = 0;

        // 获取日志行枚举源
        IAsyncEnumerable<string> logLines;
        if (failedOnly)
        {
            logLines = GetFailedJobLogsAsync(owner, repo, runId, ct);
        }
        else if (!string.IsNullOrWhiteSpace(jobId) && long.TryParse(jobId, out var jobIdLong))
        {
            logLines = _apiClient!.GetJobLogsAsync(owner, repo, jobIdLong, ct);
        }
        else if (long.TryParse(runId, out var runIdLong))
        {
            logLines = _apiClient!.GetRunLogsAsync(owner, repo, runIdLong, ct);
        }
        else
        {
            return Fail($"无效的 Run ID: {runId}");
        }

        await foreach (var line in logLines.ConfigureAwait(false))
        {
            if (markers is not null && !markers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase)))
                continue;
            // 先跳过 skipLines 行(分页续读)
            if (skipped < skipLines) { skipped++; continue; }
            matched.Add(line);
            if (matched.Count >= maxLines) break;
        }
        var prefix = BuildPrefix(runId, scope, filterLevel, matched.Count);
        if (matched.Count == 0)
            return Ok(skipLines > 0 ? $"未匹配到更多日志行(已跳过 {skipLines} 行)" : "未匹配到任何日志行", prefix);
        var text = string.Join('\n', matched);
        // 达到 maxLines 说明可能还有更多行,追加续读提示
        if (matched.Count >= maxLines)
            text += $"\n... [可能还有更多行，用 skip_lines={skipLines + maxLines} 续读]";
        if (hint is not null) text += hint;
        if (HasNoStackTrace(text))
            text += NoStackTraceHint;
        return Ok(text, prefix);
    }

    /// <summary>
    /// 跳过前 skipLines 行,再截断到 maxLines 行 — 返回 (结果文本, 是否还有更多行)
    /// <para>截断提示包含 skip_lines 续读参数,LLM 可直接分页获取后续行</para>
    /// </summary>
    private static (string text, bool hasMore) SkipAndTruncate(IReadOnlyList<string> lines, int maxLines, int skipLines)
    {
        if (lines.Count == 0) return (string.Empty, false);
        if (skipLines >= lines.Count)
            return ($"已跳过全部 {lines.Count} 行(skip_lines={skipLines})，无更多日志。", false);

        var take = Math.Min(lines.Count - skipLines, maxLines);
        var sb = new StringBuilder(take * 80);
        for (int i = skipLines; i < skipLines + take; i++)
        {
            sb.Append(lines[i]);
            sb.Append('\n');
        }
        var hasMore = skipLines + take < lines.Count;
        if (hasMore)
        {
            sb.Append($"... [共 {lines.Count} 行，显示第 {skipLines + 1}-{skipLines + take} 行。");
            sb.Append($"用 skip_lines={skipLines + take} 续读后续行]");
        }
        return (sb.ToString(), hasMore);
    }

    /// <summary>
    /// 对日志行列表应用标记过滤
    /// </summary>
    private static List<string> ApplyFilter(List<string> lines, FrozenSet<string>? markers)
    {
        if (markers is null) return lines;
        return lines.Where(l => markers.Any(m => l.Contains(m, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>
    /// 构建结果前缀
    /// </summary>
    private static string BuildPrefix(string runId, string scope, GitHubLogFilter? filterLevel, int count)
    {
        var parts = new List<string> { scope };
        if (filterLevel is { } fl) parts.Add($"过滤:{fl.ToValue()}");
        return $"Run {runId} 日志({string.Join(", ", parts)},匹配 {count} 行):";
    }

    // === 排障提示词 — 引导用户逐步缩小范围(嵌入工具返回结果) ===

    /// <summary>
    /// gh_run_list 发现失败 run 时的排障步骤提示
    /// </summary>
    private const string RunListFailureHint =
        "\n\n💡 排障步骤:\n" +
        "1. gh_run_view run_id=xxx expand=steps → 查看步骤列表\n" +
        "2. gh_run_view run_id=xxx expand=failed → 只看失败步骤\n" +
        "3. gh_run_view run_id=xxx expand=step:步骤名 filter=error → 看具体步骤的错误行";

    /// <summary>
    /// expand=steps 返回步骤列表后的下一步提示
    /// </summary>
    private const string StepsHint =
        "\n\n💡 下一步:\n" +
        "- expand=failed → 只拉失败步骤(量少)\n" +
        "- expand=step:步骤名 → 查看具体步骤日志\n" +
        "- filter=error → 只看 ##[error] 标记行";

    /// <summary>
    /// expand=step:Name 返回 section 摘要后的下一步提示(ADR 0067 Level 2)
    /// </summary>
    private const string SectionHint =
        "\n\n💡 下一步:\n" +
        "- /section:error → 查看 error 段(排障首要)\n" +
        "- /section:group → 查看 group 段(命令上下文)\n" +
        "- /section:normal → 查看普通日志(测试结果)";

    /// <summary>
    /// 日志被截断时的缩小范围提示
    /// </summary>
    private const string TruncatedHint =
        "\n\n💡 日志已截断，缩小范围:\n" +
        "- skip_lines=N → 续读后续行(截断提示中有具体值)\n" +
        "- filter=error → 只看错误行\n" +
        "- 增大 max_lines → 看更多行";

    /// <summary>
    /// expand=failed 返回失败步骤后的下一步提示
    /// </summary>
    private const string FailedHint =
        "\n\n💡 下一步: expand=step:步骤名 → 查看具体步骤的完整日志";

    /// <summary>
    /// 常规 log=true 模式的建议提示
    /// </summary>
    private const string LogHint =
        "\n\n💡 日志量大时建议:\n" +
        "- expand=steps → 按步骤展开\n" +
        "- filter=error → 只看错误行\n" +
        "- skip_lines=N → 分页续读";

    /// <summary>
    /// 缺少栈帧信息提示 — CI 只报 "Process completed with exit code 1" 但无栈帧,引导 AI 按顺序排错
    /// </summary>
    private const string NoStackTraceHint =
        "\n\n⚠️ 运行错误非 0,但缺少栈帧信息,无法定位相关错误。请按顺序逐个排错:\n" +
        "1. 死锁问题: 可能是线程锁超时但缺少抛出对应错误,请查阅工程中涉及超时的代码,修改超时时间到 5s,这样必然等候一段时间的真实死锁\n" +
        "2. CI 配置造成的问题: 检查 workflow YAML、环境变量、缓存配置是否正确\n" +
        "3. GitHub CI 异常: 重试错误发生的工程,可能是 GitHub CI 环境本身存在异常";

    /// <summary>
    /// GitHub Actions 日志过滤标记集 — 按 <see cref="GitHubLogFilter"/> 级别匹配 ##[error] / ##[warning] / ##[command]
    /// </summary>
    private static readonly FrozenSet<string> ErrorMarkers = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "##[error]");

    private static readonly FrozenSet<string> WarningMarkers = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "##[error]", "##[warning]");

    private static readonly FrozenSet<string> InfoMarkers = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "##[error]", "##[warning]", "##[command]");

    /// <summary>
    /// 获取过滤级别对应的标记集
    /// </summary>
    private static FrozenSet<string> GetFilterMarkers(GitHubLogFilter filter) => filter switch
    {
        GitHubLogFilter.Error => ErrorMarkers,
        GitHubLogFilter.Warning => WarningMarkers,
        GitHubLogFilter.Info => InfoMarkers,
        _ => ErrorMarkers,
    };

    /// <summary>
    /// 解析日志过滤级别字符串为枚举 — 无效值返回 false(走常规模式)
    /// </summary>
    private static bool TryParseLogFilter(string? filter, out GitHubLogFilter result)
    {
        result = GitHubLogFilter.All;
        if (string.IsNullOrWhiteSpace(filter)) return false;
        var parsed = GitHubLogFilterExtensions.FromValue(filter);
        if (parsed is null) return false;
        result = parsed.Value;
        return true;
    }

    /// <summary>
    /// 检测日志文本是否只有 "Process completed with exit code N" 但缺少栈帧信息
    /// <para>栈帧标记: "  at " / "Exception" / "StackTrace" / "   at " — 有任一即视为有栈帧</para>
    /// </summary>
    private static bool HasNoStackTrace(string text)
    {
        if (!text.Contains("Process completed with exit code", StringComparison.OrdinalIgnoreCase))
            return false;
        return !text.Contains("  at ", StringComparison.Ordinal)
            && !text.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("StackTrace", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("stack trace", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Section 类型的排序优先级 — error 优先(排障首要),normal 最后
    /// </summary>
    private static int SectionOrder(string type) => type switch
    {
        RunLogCache.SectionError => 0,
        RunLogCache.SectionWarning => 1,
        RunLogCache.SectionCommand => 2,
        RunLogCache.SectionGroup => 3,
        RunLogCache.SectionNormal => 4,
        _ => 5,
    };

    /// <summary>
    /// 获取 section 的预览文本 — 第一行截断到 60 字符
    /// </summary>
    private static string GetSectionPreview(List<string> lines)
    {
        if (lines.Count == 0) return string.Empty;
        var first = lines[0];
        return first.Length <= 60 ? first : first[..60] + "...";
    }

    [McpTool(GitHubToolNameConstants.GhRunRerun, "重跑 Actions Run(默认只重跑失败的 job)", "github")]
    public async Task<ToolResult> GhRunRerunAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("是否只重跑失败的 job(默认 true)", Required = false)] bool? failed_only = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var path = failed_only != false
            ? $"repos/{owner}/{repoName}/actions/runs/{run_id}/rerun-failed-jobs"
            : $"repos/{owner}/{repoName}/actions/runs/{run_id}/rerun";
        var result = await _apiClient.SendAsync(HttpMethod.Post, path, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body, $"已重跑 Run {run_id}") : Fail(result.Error);
    }

    [McpTool(GitHubToolNameConstants.GhRunCancel, "取消 Actions Run", "github")]
    public async Task<ToolResult> GhRunCancelAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var result = await _apiClient.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/actions/runs/{run_id}/cancel", ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body, $"已取消 Run {run_id}") : Fail(result.Error);
    }
}
