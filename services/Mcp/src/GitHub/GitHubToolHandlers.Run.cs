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
        [McpToolParameter("Job ID(可选,支持逗号分隔多个并行下载,如 123 或 123,456)", Required = false)] string? job_id = null,
        [McpToolParameter("是否拉取日志(默认 false,仅看详情)", Required = false)] bool? log = null,
        [McpToolParameter("最大日志行数(默认 200)", Required = false)] int? max_lines = null,
        [McpToolParameter("跳过前 N 行(用于续读截断日志,默认 0)", Required = false)] int? skip_lines = null,
        [McpToolParameter("按步骤展开: jobs=列出job列表, steps=按job_id下载日志后列出步骤, failed=只拉失败步骤, step:Name=只拉指定步骤", Required = false)] string? expand = null,
        [McpToolParameter("日志过滤级别(error/warning/info/all/failed,默认 all=不过滤;failed=智能提取测试失败+Rust风格输出)", Required = false)] string? filter = null,
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

        // === expand=jobs: 列出 job 列表(不下载日志,轻量 API 调用) ===
        if (string.Equals(expand, "jobs", StringComparison.OrdinalIgnoreCase))
        {
            return await ListJobsAsync(owner, repoName, run_id, cancellationToken).ConfigureAwait(false);
        }

        // === filter=failed: 智能过滤测试失败(状态机提取 Failed+Error+StackTrace,Rust 风格输出) ===
        if (string.Equals(filter, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return await FilterFailedTestsAsync(owner, repoName, run_id, job_id, maxLines, skip, cancellationToken);
        }

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
            // expand=steps 必须带 job_id(诱导式: 先 expand=jobs 看列表,再按需下载)
            if (wantSteps && string.IsNullOrWhiteSpace(job_id))
            {
                return Ok("expand=steps 需要指定 job_id 参数。\n\n💡 操作步骤:\n1. 先用 expand=jobs 查看 job 列表(获取 job ID 和状态)\n2. 再用 expand=steps job_id=123 下载指定 job 日志并查看步骤列表\n3. 支持逗号分隔多个 job_id 并行下载,如 job_id=123,456", "提示:");
            }

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
    /// 列出 Run 下的所有 job(不下载日志,轻量 API 调用) — 诱导式 drill-down 第一步
    /// <para>返回 job ID/名称/状态/结论,AI 选择目标 job 后用 expand=steps job_id=xxx 按需下载</para>
    /// </summary>
    private async Task<ToolResult> ListJobsAsync(string owner, string repo, string runId, CancellationToken ct)
    {
        var jobsResult = await _apiClient!.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}/jobs", paginate: true, ct: ct).ConfigureAwait(false);
        if (!jobsResult.Success) return Fail(jobsResult.Error);

        var sb = new StringBuilder();
        var failedCount = 0;
        var totalCount = 0;
        try
        {
            using var doc = JsonDocument.Parse(jobsResult.Body);
            if (!doc.RootElement.TryGetProperty("jobs", out var jobsEl))
                return Fail("未找到 jobs 数据");

            foreach (var job in jobsEl.EnumerateArray())
            {
                var id = job.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64() : 0;
                var name = job.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "unknown" : "unknown";
                var status = job.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "?" : "?";
                var conclusion = job.TryGetProperty("conclusion", out var conEl) ? conEl.GetString() ?? "" : "";
                totalCount++;

                var marker = conclusion switch
                {
                    "failure" => "❌",
                    "success" => "✅",
                    "cancelled" => "⊘",
                    _ when status == "in_progress" => "⏳",
                    _ => "  "
                };
                if (conclusion == "failure") failedCount++;

                sb.Append($"  {marker} {id,15}  {name}");
                if (!string.IsNullOrEmpty(conclusion))
                    sb.Append($"  [{conclusion}]");
                sb.Append('\n');
            }
        }
        catch (Exception ex)
        {
            return Fail($"解析 job 列表失败: {ex.Message}");
        }

        var hint = failedCount > 0
            ? $"\n\n💡 下一步:\n- expand=failed → 直接拉失败步骤日志(量少)\n- expand=steps job_id=<失败job的ID> → 下载指定 job 日志并查看步骤列表\n- 支持逗号分隔多个 job_id 并行下载,如 job_id=123,456"
            : "\n\n💡 下一步:\n- expand=steps job_id=<job ID> → 下载指定 job 日志并查看步骤列表\n- 支持逗号分隔多个 job_id 并行下载,如 job_id=123,456";

        return Ok(sb.ToString() + hint, $"Run {runId} job 列表({totalCount} 个,{failedCount} 个失败):");
    }

    /// <summary>
    /// 解析逗号分隔的 job IDs 字符串(如 "123,456")为 List{long}
    /// </summary>
    private static List<long> ParseJobIds(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return [];
        var result = new List<long>();
        foreach (var part in jobId.Split(','))
        {
            if (long.TryParse(part.Trim(), out var id))
                result.Add(id);
        }
        return result;
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

        // 3. 并行下载指定 job(s)(ADR 0067 §10) + 构建 + 缓存
        var summary = new RunLogSummary { RunId = runId, JobId = jobId };
        var sectionContents = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        var rawBuilder = new StringBuilder();

        // 解析 job_id 中的逗号分隔的多个值(如 "123,456")
        var targetJobIds = ParseJobIds(jobId);
        var parallelOk = targetJobIds.Count > 0
            && await TryDownloadJobsAsync(owner, repo, runId, targetJobIds, summary, sectionContents, rawBuilder, ct).ConfigureAwait(false);
        if (!parallelOk)
        {
            // 回退到串行: 逐个 job 下载(每个 job 独立 GitHubLogParser)
            if (targetJobIds.Count > 0)
            {
                foreach (var jobIdLong in targetJobIds)
                {
                    var parser = new GitHubLogParser();
                    await foreach (var line in _apiClient!.GetJobLogsAsync(owner, repo, jobIdLong, ct).ConfigureAwait(false))
                    {
                        rawBuilder.Append(line).Append('\n');
                        parser.ParseLine(line, summary, sectionContents);
                    }
                }
            }
            else if (long.TryParse(runId, out var runIdLong))
            {
                var parser = new GitHubLogParser();
                await foreach (var line in _apiClient!.GetRunLogsAsync(owner, repo, runIdLong, ct).ConfigureAwait(false))
                {
                    rawBuilder.Append(line).Append('\n');
                    parser.ParseLine(line, summary, sectionContents);
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
    /// 从 Level2 内容缓存获取指定 section 的日志行 — MemoryCache → 触发 Level1 填充 → 文件 raw 补填 → 再读
    /// <para>内存压力时 Level2 可被独立驱逐,下次访问时通过 Level1 触发从 .raw 文件重新解析填充</para>
    /// <para>Bug 修复: Level1 MemoryCache 命中时不填充 Level2,需从文件缓存 raw 补填</para>
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

        // Level2 仍 miss: Level1 MemoryCache 命中但未填充 Level2,从文件缓存 raw 补填
        if (!refresh)
        {
            var cacheDir = GetCacheDir(workingDir);
            var rawPath = GetCacheFilePath(cacheDir, runId, jobId, "raw");
            if (_fs.FileExists(rawPath))
            {
                try
                {
                    var rawContent = _fs.ReadAllText(rawPath);
                    FillMemoryCacheFromRaw(runId, jobId, rawContent);
                    if (_logCache.Get(sectionKey) is List<string> fileLines)
                    {
                        _logger?.LogDebug("Level2 内容缓存(文件 raw 补填)命中: {Key}, {Lines} 行", sectionKey, fileLines.Count);
                        return fileLines;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "文件 raw 补填 Level2 失败: {Path}", rawPath);
                }
            }
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
        var summary = new RunLogSummary { RunId = runId, JobId = jobId };
        var parser = new GitHubLogParser();

        foreach (var (start, length) in ranges)
        {
            if (length == 0) continue;
            var line = span.Slice(start, length).ToString();
            parser.ParseLine(line, summary, sectionContents);
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
    /// 并行下载指定 job(s) 日志(ADR 0067 §10) — 按需下载,不一次性拉全部 job
    /// <para>失败时返回 false,调用方回退到串行 GetJobLogsAsync</para>
    /// <para>并发度限制 8,避免 GitHub API 二级限速</para>
    /// </summary>
    private async Task<bool> TryDownloadJobsAsync(
        string owner, string repo, string runId, List<long> jobIds,
        RunLogSummary summary,
        Dictionary<string, Dictionary<string, List<string>>> sectionContents,
        StringBuilder rawBuilder, CancellationToken ct)
    {
        try
        {
            _logger?.LogDebug("并行下载 {Count} 个 job 日志(按需)", jobIds.Count);

            // 并行下载每个 job 日志(SemaphoreSlim 限并发 8)
            using var semaphore = new SemaphoreSlim(8);
            var tasks = jobIds.Select(async jobId =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            var lines = new List<string>();
                            await foreach (var line in _apiClient!.GetJobLogsAsync(owner, repo, jobId, ct).ConfigureAwait(false))
                            {
                                lines.Add(line);
                            }
                            if (lines.Count > 0)
                                return (jobId, lines);
                            if (attempt < 2)
                                await Task.Delay(500, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (attempt < 2)
                        {
                            _logger?.LogDebug(ex, "job {JobId} 日志下载失败,重试 {Attempt}", jobId, attempt + 1);
                            await Task.Delay(500, ct).ConfigureAwait(false);
                        }
                    }
                    return (jobId, new List<string>());
                }
                finally { semaphore.Release(); }
            }).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            // 合并日志(每个 job 独立解析,状态机跟踪步骤名)
            foreach (var (_, logLines) in results)
            {
                if (logLines.Count == 0) continue;
                var parser = new GitHubLogParser();
                foreach (var line in logLines)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    rawBuilder.Append(line).Append('\n');
                    parser.ParseLine(line, summary, sectionContents);
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
    /// GitHub Actions 日志行解析器 — 状态机 + Span,零 GC 逐行处理
    /// <para>跟踪两类步骤: ##[start-action display=...] (action 步骤) 和 ##[group]Run cmd (run 步骤)</para>
    /// <para>action 步骤优先级高于 run 步骤,action 内部的 ##[group]Run 归 action 步骤</para>
    /// <para>Span 处理时间戳剥离和标记检测,只在提取步骤名时 ToString 分配</para>
    /// </summary>
    private sealed class GitHubLogParser
    {
        private string? _currentStepName;
        private bool _inAction;
        private bool _inRunGroup;
        private static readonly SearchValues<char> s_nameTerminators = SearchValues.Create(";]");

        /// <summary>
        /// 解析一行日志并累积 — 状态机跟踪步骤名,Span 检测标记
        /// </summary>
        public void ParseLine(string line, RunLogSummary summary, Dictionary<string, Dictionary<string, List<string>>> sectionContents)
        {
            var content = StripTimestamp(line.AsSpan());

            // 优先级1: ##[start-action display=StepName;id=...]
            if (content.StartsWith("##[start-action display=".AsSpan()))
            {
                var rest = content.Slice("##[start-action display=".Length);
                var endIdx = rest.IndexOfAny(s_nameTerminators);
                _currentStepName = endIdx > 0 ? rest[..endIdx].ToString() : rest.ToString();
                _inAction = true;
                _inRunGroup = false;
                return;
            }

            // 优先级2: ##[end-action
            if (content.StartsWith("##[end-action".AsSpan()))
            {
                _currentStepName = null;
                _inAction = false;
                _inRunGroup = false;
                return;
            }

            // 优先级3: ##[group]Run cmd (仅当不在 action 中,action 内部的 group 归 action)
            if (!_inAction && content.StartsWith("##[group]Run ".AsSpan()))
            {
                var cmd = content.Slice("##[group]Run ".Length);
                _currentStepName = ExtractRunStepName(cmd);
                _inRunGroup = true;
                return;
            }

            // 优先级4: ##[endgroup] (仅当在 run group 中)
            // 不清除 _currentStepName — ##[endgroup] 只结束命令回显,实际输出在 endgroup 之后
            // 步骤持续到下一个 ##[start-action] 或 ##[group]Run 切换
            if (_inRunGroup && content.StartsWith("##[endgroup]".AsSpan()))
            {
                _inRunGroup = false;
                return;
            }

            if (_currentStepName is not null)
            {
                Accumulate(line, _currentStepName, summary, sectionContents);
                return;
            }

            // 回退: [entry.Name] 前缀 或 TSV 格式
            var stepName = TryExtractStepName(line.AsSpan());
            if (stepName is not null)
                Accumulate(line, stepName, summary, sectionContents);
        }

        /// <summary>
        /// 从 ##[group]Run 命令提取简短步骤名 — Span 处理,零 GC
        /// <para>"dotnet test xxx.csproj ..." → "dotnet test xxx"</para>
        /// <para>"dotnet build xxx.csproj ..." → "dotnet build xxx"</para>
        /// <para>"actions/checkout@v5" → "actions/checkout@v5"</para>
        /// <para>"./.github/actions/setup-test-env" → "setup-test-env"</para>
        /// <para>其他 → 截断到 60 字符</para>
        /// </summary>
        private static string ExtractRunStepName(ReadOnlySpan<char> cmd)
        {
            // dotnet test xxx.csproj ... → dotnet test xxx
            if (cmd.StartsWith("dotnet test ".AsSpan()))
            {
                var after = cmd.Slice("dotnet test ".Length);
                var csprojIdx = after.IndexOf(".csproj".AsSpan());
                if (csprojIdx > 0)
                {
                    var path = after[..csprojIdx];
                    var lastSlash = path.LastIndexOf('/');
                    var shortName = lastSlash >= 0 ? path.Slice(lastSlash + 1) : path;
                    return string.Concat("dotnet test ", shortName.ToString());
                }
                return "dotnet test";
            }

            // dotnet build xxx.csproj ... → dotnet build xxx
            if (cmd.StartsWith("dotnet build ".AsSpan()))
            {
                var after = cmd.Slice("dotnet build ".Length);
                var csprojIdx = after.IndexOf(".csproj".AsSpan());
                if (csprojIdx > 0)
                {
                    var path = after[..csprojIdx];
                    var lastSlash = path.LastIndexOf('/');
                    var shortName = lastSlash >= 0 ? path.Slice(lastSlash + 1) : path;
                    return string.Concat("dotnet build ", shortName.ToString());
                }
                return "dotnet build";
            }

            // ./.github/actions/xxx → xxx
            if (cmd.StartsWith("./.github/actions/".AsSpan()))
            {
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
        private static ReadOnlySpan<char> StripTimestamp(ReadOnlySpan<char> span)
        {
            // 时间戳格式: "2026-09-07T17:08:27.5016453Z content"
            var zIdx = span.IndexOf('Z');
            if (zIdx > 0 && zIdx + 2 < span.Length && span[zIdx + 1] == ' ')
                return span.Slice(zIdx + 2);
            // [entry.Name] content
            if (span.Length > 0 && span[0] == '[')
            {
                var closeIdx = span.IndexOf(']');
                if (closeIdx > 0 && closeIdx + 2 < span.Length)
                    return span.Slice(closeIdx + 2);
            }
            return span;
        }

        /// <summary>
        /// 从日志行 Span 提取步骤名 — [entry.Name] 前缀优先,回退 TSV,只在找到时 ToString
        /// </summary>
        private static string? TryExtractStepName(ReadOnlySpan<char> span)
        {
            // [entry.Name] line → ExtractStepNameFromEntryName(entry.Name)
            if (span.Length > 0 && span[0] == '[')
            {
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
        private static string ExtractStepNameFromEntryName(ReadOnlySpan<char> entryName)
        {
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
        secLines.Add(StripLogTimestamp(line));
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
    /// 智能过滤测试失败行 — 状态机提取 Failed + Error Message + Stack Trace,Rust 风格输出
    /// <para>状态机: Normal → InFailedTest(遇到 Failed/[FAIL]) → InErrorMessage(Error Message:) → InStackTrace(Stack Trace:) → Normal</para>
    /// <para>输出: 每个失败测试用 --> line N 指示, | 管道符标注日志行, = 总结行</para>
    /// </summary>
    private async Task<ToolResult> FilterFailedTestsAsync(
        string owner, string repo, string runId, string? jobId,
        int maxLines, int skipLines, CancellationToken ct)
    {
        // 获取日志行枚举源(优先失败 job,其次指定 job,最后整个 run)
        IAsyncEnumerable<string> logLines;
        if (string.IsNullOrWhiteSpace(jobId))
        {
            logLines = GetFailedJobLogsAsync(owner, repo, runId, ct);
        }
        else if (long.TryParse(jobId, out var jobIdLong))
        {
            logLines = _apiClient!.GetJobLogsAsync(owner, repo, jobIdLong, ct);
        }
        else
        {
            logLines = _apiClient!.GetRunLogsAsync(owner, repo, long.Parse(runId), ct);
        }

        // 状态机解析
        var failures = new List<TestFailureInfo>();
        TestFailureInfo? current = null;
        var state = LogParseState.Normal;
        var lineNumber = 0;

        await foreach (var line in logLines.ConfigureAwait(false))
        {
            lineNumber++;
            var content = StripLogTimestamp(line);

            switch (state)
            {
                case LogParseState.Normal:
                    // 检测测试失败标记: "  Failed xxx [FAIL]" 或 "[xUnit.net] xxx [FAIL]"
                    if (content.Contains("[FAIL]", StringComparison.OrdinalIgnoreCase) ||
                        content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase))
                    {
                        current = new TestFailureInfo { StartLine = lineNumber, TestLine = content };
                        failures.Add(current);
                        state = LogParseState.InFailedTest;
                    }
                    // 检测 ##[error] 行
                    else if (content.Contains("##[error]", StringComparison.OrdinalIgnoreCase))
                    {
                        current = new TestFailureInfo { StartLine = lineNumber, TestLine = content, IsErrorMarker = true };
                        failures.Add(current);
                        state = LogParseState.Normal;
                    }
                    break;

                case LogParseState.InFailedTest:
                    if (content.StartsWith("  Error Message:", StringComparison.OrdinalIgnoreCase))
                    {
                        state = LogParseState.InErrorMessage;
                    }
                    else if (content.StartsWith("  Stack Trace:", StringComparison.OrdinalIgnoreCase))
                    {
                        state = LogParseState.InStackTrace;
                    }
                    else if (content.StartsWith("  Passed ", StringComparison.OrdinalIgnoreCase) ||
                             content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase) ||
                             content.Contains("[PASS]", StringComparison.OrdinalIgnoreCase))
                    {
                        state = LogParseState.Normal;
                        current = null;
                    }
                    break;

                case LogParseState.InErrorMessage:
                    if (content.StartsWith("  Stack Trace:", StringComparison.OrdinalIgnoreCase))
                    {
                        state = LogParseState.InStackTrace;
                    }
                    else if (content.StartsWith("  Passed ", StringComparison.OrdinalIgnoreCase) ||
                             content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase))
                    {
                        state = LogParseState.Normal;
                        current = null;
                    }
                    else if (current is not null)
                    {
                        current.ErrorMessageLines.Add(content.Trim());
                    }
                    break;

                case LogParseState.InStackTrace:
                    if (current is not null)
                    {
                        current.StackTraceLines.Add(content);
                    }
                    if (content.StartsWith("  Passed ", StringComparison.OrdinalIgnoreCase) ||
                        content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase) ||
                        content.Contains("--- End of stack trace", StringComparison.OrdinalIgnoreCase))
                    {
                        state = LogParseState.Normal;
                        current = null;
                    }
                    break;
            }
        }

        if (failures.Count == 0)
        {
            return Ok("未检测到测试失败行。尝试用 filter=error 看 ##[error] 标记,或 log=true 看完整日志。", $"Run {runId} 测试失败过滤(0 个):");
        }

        // Rust 风格输出
        var sb = new StringBuilder();
        var shown = 0;
        foreach (var f in failures.Skip(skipLines))
        {
            if (shown >= maxLines) break;
            shown++;
            sb.Append(f.FormatRustStyle());
            sb.Append('\n');
        }

        var prefix = $"Run {runId} 测试失败({failures.Count} 个,显示 {shown} 个)";
        if (skipLines > 0) prefix += $",跳过前 {skipLines} 个";
        if (skipLines + shown < failures.Count)
            sb.Append($"\n... [共 {failures.Count} 个失败,用 skip_lines={skipLines + shown} 续读]");
        return Ok(sb.ToString(), prefix);
    }

    /// <summary>
    /// 去掉日志行的时间戳前缀和 ANSI 转义码 — "2026-09-07T17:08:27.5016453Z \x1B[36;1mcontent\x1B[0m" → "content"
    /// </summary>
    private static string StripLogTimestamp(string line)
    {
        // GitHub Actions 日志格式: "2026-09-07T17:08:27.5016453Z content"
        // 找到第一个 'Z ' 后面的内容
        var zIdx = line.IndexOf('Z');
        if (zIdx > 0 && zIdx + 2 < line.Length && line[zIdx + 1] == ' ')
        {
            return StripAnsiEscapes(line[(zIdx + 2)..]);
        }
        // [entry.Name] 前缀的行
        if (line.StartsWith('['))
        {
            var closeIdx = line.IndexOf(']');
            if (closeIdx > 0 && closeIdx + 2 < line.Length)
                return StripAnsiEscapes(line[(closeIdx + 2)..]);
        }
        return StripAnsiEscapes(line);
    }

    /// <summary>
    /// 去除 ANSI 转义码序列(ESC[...m) — Span 查找 ESC,无 ESC 直接返回零分配
    /// </summary>
    private static string StripAnsiEscapes(string s)
    {
        var span = s.AsSpan();
        var escIdx = span.IndexOf('\x1B');
        if (escIdx < 0) return s;
        var sb = new StringBuilder(s.Length);
        var i = 0;
        while (i < span.Length)
        {
            if (span[i] == '\x1B' && i + 1 < span.Length && span[i + 1] == '[')
            {
                i += 2;
                while (i < span.Length && span[i] != 'm') i++;
                i++;
            }
            else
            {
                sb.Append(span[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 日志解析状态机状态
    /// </summary>
    private enum LogParseState
    {
        Normal,         // 普通行
        InFailedTest,   // 遇到 Failed/[FAIL],等待 Error Message 或 Stack Trace
        InErrorMessage, // 在 Error Message: 之后
        InStackTrace,   // 在 Stack Trace: 之后
    }

    /// <summary>
    /// 测试失败信息 — 用于 Rust 风格输出
    /// </summary>
    private sealed class TestFailureInfo
    {
        public int StartLine;
        public string TestLine = "";
        public List<string> ErrorMessageLines = [];
        public List<string> StackTraceLines = [];
        public bool IsErrorMarker;

        /// <summary>
        /// Rust 风格格式化 — --> line N 指示, | 管道符标注日志行, = 总结行
        /// </summary>
        public string FormatRustStyle()
        {
            var sb = new StringBuilder();
            sb.Append($"--> line {StartLine}");
            sb.Append('\n');
            sb.Append("   |");
            sb.Append('\n');
            sb.Append($"   | {TestLine.Trim()}");
            sb.Append('\n');
            if (ErrorMessageLines.Count > 0)
            {
                sb.Append("   |   Error Message:");
                sb.Append('\n');
                foreach (var em in ErrorMessageLines)
                {
                    sb.Append($"   |     {em}");
                    sb.Append('\n');
                }
            }
            if (StackTraceLines.Count > 0)
            {
                sb.Append("   |   Stack Trace:");
                sb.Append('\n');
                foreach (var st in StackTraceLines.Take(10))
                {
                    sb.Append($"   | {st.Trim()}");
                    sb.Append('\n');
                }
                if (StackTraceLines.Count > 10)
                    sb.Append($"   | ... ({StackTraceLines.Count - 10} 行未显示)");
            }
            sb.Append("   |");
            sb.Append('\n');
            // 总结行
            if (!IsErrorMarker && ErrorMessageLines.Count > 0)
            {
                var testName = ExtractTestNameFromLine(TestLine);
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

        private static string? ExtractTestNameFromLine(string line)
        {
            // 先去掉时间戳前缀 "2026-09-07T17:09:52.2828405Z content"
            var content = StripLogTimestamp(line).TrimStart();
            // "  Failed Mcp.Tests.xxx [24 ms]" → "Mcp.Tests.xxx"
            // "[xUnit.net 00:00:00.81]     Mcp.Tests.xxx [FAIL]" → "Mcp.Tests.xxx"
            if (content.StartsWith("Failed ", StringComparison.OrdinalIgnoreCase))
                content = content[7..];
            if (content.StartsWith("[xUnit.net", StringComparison.OrdinalIgnoreCase))
            {
                var bracketEnd = content.IndexOf(']');
                if (bracketEnd > 0) content = content[(bracketEnd + 1)..].TrimStart();
            }
            // 截取到 [ 之前
            var bracketIdx = content.IndexOf('[');
            if (bracketIdx > 0) content = content[..bracketIdx].Trim();
            return string.IsNullOrEmpty(content) ? null : content;
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
        var lineNumber = 0;

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
            lineNumber++;
            if (markers is not null && !markers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase)))
                continue;
            // 先跳过 skipLines 行(分页续读)
            if (skipped < skipLines) { skipped++; continue; }
            // 加行号前缀,方便定位(去时间戳减少噪音)
            matched.Add($"  L{lineNumber,5}  {StripLogTimestamp(line)}");
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
        "1. gh_run_view run_id=xxx expand=jobs → 查看 job 列表(轻量,不下载日志)\n" +
        "2. gh_run_view run_id=xxx expand=failed → 直接拉失败步骤日志\n" +
        "3. gh_run_view run_id=xxx expand=steps job_id=<失败job的ID> → 下载指定 job 日志并查看步骤\n" +
        "4. gh_run_view run_id=xxx log=true job_id=<ID> filter=error → 只看错误行";

    /// <summary>
    /// expand=steps 返回步骤列表后的下一步提示
    /// </summary>
    private const string StepsHint =
        "\n\n💡 下一步:\n" +
        "- expand=step:步骤名 → 查看具体步骤日志\n" +
        "- filter=error → 只看 ##[error] 标记行\n" +
        "- log=true job_id=xxx filter=error → 直接过滤错误行";

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
