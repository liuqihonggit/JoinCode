namespace McpToolDispatch;

/// <summary>
/// GitHub Actions Run 工具 — gh run 子命令全套
/// <para>避坑2/3/5: 大日志 maxLines 截断 + --job 精准拉 + 30s 超时</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 列出 Actions Run — 支持状态/分支过滤，发现失败 run 时附排障步骤提示
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunList, "列出 Actions Run(支持状态/分支过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunListAsync(
        [McpToolParameter("数量限制(默认 20)", Required = false)] int? limit = null,
        [McpToolParameter("状态过滤(queued/in_progress/completed,可选)", Required = false)] string? status = null,
        [McpToolParameter("分支过滤(可选)", Required = false)] string? branch = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 20).ToString() };
        if (!string.IsNullOrWhiteSpace(status)) query["status"] = status;
        if (!string.IsNullOrWhiteSpace(branch)) query["branch"] = branch;

        var result = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/runs", query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);

        var summarized = GitHubRunListSummarizer.SummarizeRunList(result.Body);
        var hasFailure = result.Body.Contains("\"conclusion\":\"failure\"", StringComparison.OrdinalIgnoreCase);
        return hasFailure ? Ok(summarized + GitHubRunLogHints.RunListFailureHint) : Ok(summarized);
    }


    /// <summary>
    /// 查看 Run 详情/日志 — 支持 expand 按步骤展开（两级缓存跨进程）、filter 按标记过滤、skip_lines 分页续读、refresh 强制刷新
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunView, "查看 Run 详情/日志(expand 按步骤展开+文件级缓存跨进程,filter 按标记过滤,skip_lines 分页续读,refresh 强制刷新)", "github", ConcurrencySafe = true)]
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
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var maxLines = max_lines ?? 200;
        var skip = skip_lines ?? 0;
        var wantRefresh = refresh == true;
        // MCP 框架可能把缺失的 string? 参数传成空字符串,统一归一化为 null
        job_id = string.IsNullOrWhiteSpace(job_id) ? null : job_id;
        var hasFilter = GitHubRunLogFilter.TryParseLogFilter(filter, out var filterLevel) && filterLevel != GitHubLogFilter.All;
        var markers = hasFilter ? GitHubRunLogFilter.GetFilterMarkers(filterLevel) : null;

        // === expand=jobs: 列出 job 列表(不下载日志,轻量 API 调用) ===
        if (string.Equals(expand, "jobs", StringComparison.OrdinalIgnoreCase)) {
            return await ListJobsAsync(owner, repoName, run_id, cancellationToken).ConfigureAwait(false);
        }

        // === filter=failed: 智能过滤测试失败(状态机提取 Failed+Error+StackTrace,Rust 风格输出) ===
        if (string.Equals(filter, "failed", StringComparison.OrdinalIgnoreCase)) {
            return await FilterFailedTestsAsync(owner, repoName, run_id, job_id, maxLines, skip, cancellationToken).ConfigureAwait(false);
        }

        // === expand=failed: 只拉失败步骤日志(量少,不缓存) ===
        if (string.Equals(expand, "failed", StringComparison.OrdinalIgnoreCase)) {
            return await StreamAndFilterAsync(owner, repoName, run_id, job_id, true, "失败步骤", markers, filterLevel, maxLines, cancellationToken, GitHubRunLogHints.FailedHint, skip).ConfigureAwait(false);
        }

        // === expand=steps 或 expand=step:Name: 两级缓存(ADR 0067) ===
        var expandStep = expand?.StartsWith("step:", StringComparison.OrdinalIgnoreCase) == true
            ? expand[5..].Trim()
            : null;
        var wantSteps = string.Equals(expand, "steps", StringComparison.OrdinalIgnoreCase);

        if (wantSteps || expandStep is not null) {
            // expand=steps 必须带 job_id(诱导式: 先 expand=jobs 看列表,再按需下载)
            if (wantSteps && string.IsNullOrWhiteSpace(job_id)) {
                return Ok("expand=steps 需要指定 job_id 参数。\n\n💡 操作步骤:\n1. 先用 expand=jobs 查看 job 列表(获取 job ID 和状态)\n2. 再用 expand=steps job_id=123 下载指定 job 日志并查看步骤列表\n3. 支持逗号分隔多个 job_id 并行下载,如 job_id=123,456", "提示:");
            }

            // 解析 /section:Type 后缀
            string? sectionType = null;
            if (expandStep is not null) {
                var sectionIdx = expandStep.IndexOf("/section:", StringComparison.OrdinalIgnoreCase);
                if (sectionIdx >= 0) {
                    sectionType = expandStep[(sectionIdx + 9)..].Trim();
                    expandStep = expandStep[..sectionIdx].Trim();
                }
            }

            // expand=step:Name/section:Type: 从 Level2 内容缓存读取(ADR 0067)
            if (expandStep is not null && sectionType is not null) {
                var sectionLines = await GetOrFetchSectionAsync(owner, repoName, run_id, job_id, expandStep, sectionType, working_dir, wantRefresh, cancellationToken).ConfigureAwait(false);
                if (sectionLines is null)
                    return Ok($"未找到步骤 '{expandStep}' 或 section '{sectionType}'，建议先 expand=step:{expandStep} 查看 section 摘要");

                var (secText, secHasMore) = GitHubRunLogFilter.SkipAndTruncate(sectionLines, maxLines, skip);
                if (secHasMore)
                    secText += GitHubRunLogHints.TruncatedHint;
                if (GitHubRunLogFilter.HasNoStackTrace(secText))
                    secText += GitHubRunLogHints.NoStackTraceHint;
                var secPrefix = GitHubRunLogFilter.BuildPrefix(run_id, $"步骤:{expandStep}/section:{sectionType}", filterLevel, sectionLines.Count);
                return Ok(secText, secPrefix);
            }

            // 其余情况(expand=steps 或 expand=step:Name): 从 Level1 摘要缓存读取
            var summary = await GetOrFetchSummaryAsync(owner, repoName, run_id, job_id, working_dir, wantRefresh, cancellationToken).ConfigureAwait(false);
            if (summary is null) return Fail("日志拉取失败");

            // expand=steps: 返回步骤列表(有 error 的步骤标 ❌)
            if (wantSteps) {
                var stepsText = summary.StepLineCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Select(kvp => {
                        var hasError = summary.SectionCounts.TryGetValue(kvp.Key, out var secs)
                            && secs.TryGetValue(RunLogCache.SectionError, out _);
                        var marker = hasError ? "❌ " : "   ";
                        return $"  {marker}{kvp.Value,6} 行  {kvp.Key}";
                    });
                return Ok(string.Join('\n', stepsText) + GitHubRunLogHints.StepsHint, $"Run {run_id} 步骤列表({summary.StepLineCounts.Count} 步骤,缓存于 {summary.CachedAt:HH:mm:ss}):");
            }

            // expand=step:Name: 返回 section 摘要(Level 2,ADR 0067)
            if (expandStep is not null) {
                if (!summary.SectionCounts.TryGetValue(expandStep, out var secs))
                    return Ok($"未找到步骤 '{expandStep}'，可用步骤: {string.Join(", ", summary.SectionCounts.Keys)}");

                var summaryText = secs
                    .OrderBy(kvp => GitHubRunLogFilter.SectionOrder(kvp.Key))
                    .Select(kvp => $"  {kvp.Key,-8} {kvp.Value,5} 行  (用 expand=step:{expandStep}/section:{kvp.Key} 查看)");
                return Ok(string.Join('\n', summaryText) + GitHubRunLogHints.SectionHint, $"Run {run_id} 步骤:{expandStep} sections({secs.Count} 类):");
            }
        }

        // === 常规模式: log=false 看详情, log=true 拉日志 ===
        var wantLog = log == true;

        if (wantLog) {
            // log=true: 用 REST API 日志流 + 过滤/分页
            return await StreamAndFilterAsync(owner, repoName, run_id, job_id, false, "日志", markers, filterLevel, maxLines, cancellationToken, GitHubRunLogHints.LogHint, skip).ConfigureAwait(false);
        }

        // log=false: 获取 run 详情 JSON
        var detailResult = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/runs/{run_id}", ct: cancellationToken).ConfigureAwait(false);
        return detailResult.Success ? Ok(detailResult.Body) : Fail(detailResult.Error);
    }

    /// <summary>
    /// 列出 Run 下的所有 job(不下载日志,轻量 API 调用) — 诱导式 drill-down 第一步
    /// <para>返回 job ID/名称/状态/结论,AI 选择目标 job 后用 expand=steps job_id=xxx 按需下载</para>
    /// </summary>
    private Task<ToolResult> ListJobsAsync(string owner, string repo, string runId, CancellationToken ct)
        => _logFetcher.ListJobsAsync(owner, repo, runId, ct);


    /// <summary>
    /// 从 Level1 摘要缓存获取或流式拉取 — 三级缓存: MemoryCache → 文件级缓存(.jcc/gh_cache/) → 下载
    /// <para>文件级缓存跨进程共享,updatedAt 验证检测 rerun 脏数据,Actor 管道异步写入不阻塞</para>
    /// <para>ADR 0067 两级缓存 + 文件级持久化: 摘要(轻量)+内容(大量行)按 section 独立缓存</para>
    /// </summary>
    private Task<RunLogSummary?> GetOrFetchSummaryAsync(string owner, string repo, string runId, string? jobId, string? workingDir, bool refresh, CancellationToken ct)
        => _logCacheService!.GetOrFetchSummaryAsync(owner, repo, runId, jobId, workingDir, refresh, ct);

    /// <summary>
    /// 从 Level2 内容缓存获取指定 section 的日志行 — MemoryCache → 触发 Level1 填充 → 文件 raw 补填 → 再读
    /// <para>内存压力时 Level2 可被独立驱逐,下次访问时通过 Level1 触发从 .raw 文件重新解析填充</para>
    /// <para>Bug 修复: Level1 MemoryCache 命中时不填充 Level2,需从文件缓存 raw 补填</para>
    /// </summary>
    private Task<List<string>?> GetOrFetchSectionAsync(
        string owner, string repo, string runId, string? jobId, string stepName, string sectionType,
        string? workingDir, bool refresh, CancellationToken ct)
        => _logCacheService!.GetOrFetchSectionAsync(owner, repo, runId, jobId, stepName, sectionType, workingDir, refresh, ct);

    /// <summary>
    /// 从 GitHub REST API 获取 Run 的 updated_at — 用于检测 rerun 后日志是否更新
    /// <para>轻量 API 调用(不下载日志),&lt; 1s</para>
    /// </summary>
    private Task<string?> FetchUpdatedAtAsync(string owner, string repo, string runId, CancellationToken ct)
        => _logCacheService!.FetchUpdatedAtAsync(owner, repo, runId, ct);



    /// <summary>
    /// 获取指定 Run 中所有失败 job 的日志 — 逐行 yield(合并多个 job 日志)
    /// <para>用于 expand=failed 模式,只拉 conclusion=failure 的 job 日志</para>
    /// </summary>
    private IAsyncEnumerable<string> GetFailedJobLogsAsync(
        string owner, string repo, string runId,
        CancellationToken ct)
        => _logFilterRunner!.GetFailedJobLogsAsync(owner, repo, runId, ct);
    /// <summary>
    /// 智能过滤测试失败行 — 状态机提取 Failed + Error Message + Stack Trace,Rust 风格输出
    /// <para>状态机: Normal → InFailedTest(遇到 Failed/[FAIL]) → InErrorMessage(Error Message:) → InStackTrace(Stack Trace:) → Normal</para>
    /// <para>输出: 每个失败测试用 --> line N 指示, | 管道符标注日志行, = 总结行</para>
    /// </summary>
    private Task<ToolResult> FilterFailedTestsAsync(
        string owner, string repo, string runId, string? jobId,
        int maxLines, int skipLines, CancellationToken ct)
        => _logFilterRunner!.FilterFailedTestsAsync(owner, repo, runId, jobId, maxLines, skipLines, ct);

    /// <summary>
    /// 流式拉取 + 过滤 + 分页跳过(不缓存,用于 --log-failed 或一次性过滤)
    /// <para>日志源: failedOnly=true → 失败 job 日志; jobId 有值 → 单 job 日志; 否则 → 整个 run 日志</para>
    /// </summary>
    private Task<ToolResult> StreamAndFilterAsync(
        string owner, string repo, string runId, string? jobId, bool failedOnly,
        string scope, FrozenSet<string>? markers, GitHubLogFilter? filterLevel,
        int maxLines, CancellationToken ct, string? hint = null, int skipLines = 0)
        => _logFilterRunner!.StreamAndFilterAsync(owner, repo, runId, jobId, failedOnly, scope, markers, filterLevel, maxLines, ct, hint, skipLines);

    /// <summary>
    /// 重跑 Actions Run — 默认只重跑失败的 job，调 REST API POST rerun-failed-jobs 或 rerun
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunRerun, "重跑 Actions Run(默认只重跑失败的 job)", "github")]
    public async Task<ToolResult> GhRunRerunAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("是否只重跑失败的 job(默认 true)", Required = false)] bool? failed_only = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
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

    /// <summary>
    /// 取消 Actions Run — 调 REST API POST cancel
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunCancel, "取消 Actions Run", "github")]
    public async Task<ToolResult> GhRunCancelAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var result = await _apiClient.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/actions/runs/{run_id}/cancel", ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body, $"已取消 Run {run_id}") : Fail(result.Error);
    }
}