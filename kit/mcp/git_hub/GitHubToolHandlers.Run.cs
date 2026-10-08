namespace McpToolDispatch;

/// <summary>
/// GitHub Actions Run 工具 — gh run 子命令全套
/// <para>避坑2/3/5: 大日志 maxLines 截断 + --job 精准拉 + 30s 超时</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 列出 Actions Run — 支持状态/分支/事件/工作流/用户/commit/创建时间过滤，发现失败 run 时附排障步骤提示
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunList, "列出 Actions Run(支持状态/分支/事件/工作流/用户/commit/创建时间过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunListAsync(
        [McpToolParameter("数量限制(默认 20)", Required = false)] int? limit = null,
        [McpToolParameter("状态过滤(queued/in_progress/completed,可选)", Required = false)] string? status = null,
        [McpToolParameter("分支过滤(可选)", Required = false)] string? branch = null,
        [McpToolParameter("事件过滤(可选,如 push/pull_request/schedule)", Required = false)] string? event_type = null,
        [McpToolParameter("工作流名或 ID(可选,如 ci.yml)", Required = false)] string? workflow = null,
        [McpToolParameter("触发者过滤(可选)", Required = false)] string? user = null,
        [McpToolParameter("commit SHA 过滤(可选)", Required = false)] string? commit = null,
        [McpToolParameter("创建时间过滤(可选,如 >2026-01-01)", Required = false)] string? created = null,
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 id,status,conclusion)", Required = false)] string? json_fields = null,
        [McpToolParameter("输出档位(0=gh风格表格[默认] 1=精简JSON 2=完整JSON)", Required = false)] int? verbosity = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 20).ToString() };
            if (!string.IsNullOrWhiteSpace(status)) query["status"] = status;
            if (!string.IsNullOrWhiteSpace(branch)) query["branch"] = branch;
            if (!string.IsNullOrWhiteSpace(event_type)) query["event"] = event_type;
            if (!string.IsNullOrWhiteSpace(user)) query["actor"] = user;
            if (!string.IsNullOrWhiteSpace(commit)) query["head_sha"] = commit;
            if (!string.IsNullOrWhiteSpace(created)) query["created"] = created;
            var basePath = string.IsNullOrWhiteSpace(workflow)
                ? $"repos/{owner}/{repoName}/actions/runs"
                : $"repos/{owner}/{repoName}/actions/workflows/{workflow}/runs";
            var result = await client.SendAsync(HttpMethod.Get, basePath, query: query, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(BuildRunListErrorHint(result, workflow));
            if (!string.IsNullOrEmpty(json_fields)) {
                var filtered = FilterJsonFields(result.Body, json_fields);
                if (filtered is "[]" or "{}")
                    return Ok(filtered + "\n\n⚠️ 返回空: 可能原因: ① 字段名不匹配(用 verbosity=2 查看完整字段) ② 该 run 无此字段");
                return Ok(filtered);
            }
            var hasFailure = result.Body.Contains("\"conclusion\":\"failure\"", StringComparison.OrdinalIgnoreCase);
            var failureHint = hasFailure ? GitHubRunLogHints.RunListFailureHint : "";
            return verbosity switch {
                2 => Ok(result.Body + failureHint),
                1 => Ok(GitHubRunListSummarizer.SummarizeRunList(result.Body) + failureHint),
                _ => Ok(GitHubRunListSummarizer.SummarizeRunListBrief(result.Body) + failureHint)
            };
        }).ConfigureAwait(false);


    /// <summary>
    /// 构建 run list 错误提示 — 404/Not Found 时给出可能原因和引导（缺陷5）
    /// </summary>
    private static string BuildRunListErrorHint(GitHubApiResponse result, string? workflow) {
        var baseHint = result.StatusCode == 404
            ? "404 Not Found — 可能原因:"
              + (string.IsNullOrWhiteSpace(workflow) ? "" : $" ① 工作流名称 '{workflow}' 不匹配(用 gh workflow list 查看可用工作流)")
              + " ② 当前目录不是 git 仓库根目录 ③ 仓库不存在或无权限"
            : result.Error;
        return baseHint;
    }

    /// <summary>
    /// 查看 Run 详情/日志 — 支持 expand 按步骤展开（两级缓存跨进程）、filter 按标记过滤、skip_lines 分页续读、refresh 强制刷新、web 返回 URL、attempt 指定重试次数
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunView, "查看 Run 详情/日志(expand 按步骤展开+文件级缓存跨进程,filter 按标记过滤,skip_lines 分页续读,refresh 强制刷新,web 返回 URL,attempt 指定重试次数)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunViewAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("Job ID(可选,支持逗号分隔多个并行下载,如 123 或 123,456)", Required = false)] string? job_id = null,
        [McpToolParameter("是否拉取日志(默认 false,仅看详情)", Required = false)] bool? log = null,
        [McpToolParameter("最大日志行数(默认 200)", Required = false)] int? max_lines = null,
        [McpToolParameter("跳过前 N 行(用于续读截断日志,默认 0)", Required = false)] int? skip_lines = null,
        [McpToolParameter("按步骤展开: jobs=列出job列表, steps=按job_id下载日志后列出步骤, failed=只拉失败步骤, step:Name=只拉指定步骤", Required = false)] string? expand = null,
        [McpToolParameter("日志过滤级别(error/warning/info/all/failed,默认 all=不过滤;failed=智能提取测试失败+Rust风格输出)", Required = false)] string? filter = null,
        [McpToolParameter("强制刷新缓存(默认 false,rerun 后用 true 避免脏数据)", Required = false)] bool? refresh = null,
        [McpToolParameter("web=true 只返回 Run 浏览器 URL", Required = false)] bool? web = null,
        [McpToolParameter("重试次数(可选,查看指定 attempt 的详情)", Required = false)] int? attempt = null,
        [McpToolParameter("log_failed=true 只拉失败步骤日志(等价于 --expand failed --log,系统 gh CLI --log-failed 缩写)", Required = false)] bool? log_failed = null,
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 id,status,conclusion)", Required = false)] string? json_fields = null,
        [McpToolParameter("输出档位(0=gh风格简洁[默认] 1=精简JSON 2=完整JSON)", Required = false)] int? verbosity = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            // 宽容: AI 可能传 run number(如 752)而非 run id(如 37663049294),自动解析
            var resolvedRunId = await ResolveRunIdAsync(client, owner, repoName, run_id, cancellationToken).ConfigureAwait(false);
            if (resolvedRunId is null)
                return Fail($"Run '{run_id}' 不存在。可能原因: ① run id 错误 ② run number 无对应 run。提示: 用 gh run list 查看 ID 列(11位数字),非 NUM 列");
            if (web == true) {
                var runResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/runs/{resolvedRunId}", ct: cancellationToken).ConfigureAwait(false);
                if (!runResult.Success) return Fail(runResult.Error);
                var url = ExtractHtmlUrl(runResult.Body);
                return string.IsNullOrEmpty(url) ? Fail("无法从 Run 响应中解析 html_url") : Ok(url);
            }
            if (!string.IsNullOrEmpty(json_fields)) {
                var runResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/runs/{resolvedRunId}", ct: cancellationToken).ConfigureAwait(false);
                if (!runResult.Success) return Fail(runResult.Error);
                return Ok(FilterJsonFields(runResult.Body, json_fields));
            }
            if (log_failed == true) {
                expand = "failed";
                log = true;
            }
            return await GhRunViewCoreAsync(client, owner, repoName, resolvedRunId, job_id, log, max_lines, skip_lines, expand, filter, refresh, attempt, working_dir, verbosity, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

    /// <summary>
    /// 解析 run_id — 宽容接受 run number(短数字)自动查找对应 run id（缺陷3）
    /// <para>AI 可能用 run list 的 NUM 列(如 752)而非 ID 列(如 37663049294),先尝试直接查询,
    /// 404 且为短数字时按 run_number 搜索。</para>
    /// </summary>
    private async Task<string?> ResolveRunIdAsync(IGitHubApiClient client, string owner, string repoName, string runId, CancellationToken ct) {
        // 先尝试直接用 run_id 查询
        var direct = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/runs/{runId}", ct: ct).ConfigureAwait(false);
        if (direct.Success) return runId;

        // 404 且 run_id 是短数字(看起来像 run number)时,按 run_number 搜索
        if (direct.StatusCode != 404 || !IsLikelyRunNumber(runId)) return null;

        var listResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/runs",
            query: new Dictionary<string, string> { ["per_page"] = "100" }, ct: ct).ConfigureAwait(false);
        if (!listResult.Success) return null;

        return FindRunIdByNumber(listResult.Body, runId, _logger);
    }

    /// <summary>run_id 通常是 10-11 位数字,run number 是短数字(少于 10 位)</summary>
    private static bool IsLikelyRunNumber(string runId)
        => runId.Length > 0 && runId.Length < 10 && runId.All(char.IsDigit);

    /// <summary>从 run list JSON 中查找指定 run_number 对应的 run id</summary>
    private static string? FindRunIdByNumber(string json, string number, ILogger? logger) {
        try {
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.RunNumberLookupListResponse);
            if (resp is null) return null;
            foreach (var run in resp.WorkflowRuns) {
                if (run.RunNumber.ToString() == number)
                    return run.Id.ToString();
            }
        } catch (JsonException ex) {
            logger?.LogWarning(ex, "解析 run list JSON 失败,无法按 run_number 查找");
        }
        return null;
    }

    /// <summary>
    /// GhRunView 核心逻辑 — expand/filter/log 多分支调度,两级缓存(ADR 0067)
    /// </summary>
    private async Task<ToolResult> GhRunViewCoreAsync(IGitHubApiClient client, string owner, string repoName, string run_id, string? job_id, bool? log, int? max_lines, int? skip_lines, string? expand, string? filter, bool? refresh, int? attempt, string? working_dir, int? verbosity, CancellationToken cancellationToken) {
        var maxLines = max_lines ?? 200;
        var skip = skip_lines ?? 0;
        var wantRefresh = refresh == true;
        // MCP 框架可能把缺失的 string? 参数传成空字符串,统一归一化为 null
        job_id = string.IsNullOrWhiteSpace(job_id) ? null : job_id;
        var hasFilter = GitHubRunLogFilter.TryParseLogFilter(filter, out var filterLevel) && filterLevel != GitHubLogFilter.None;
        var markers = hasFilter ? GitHubRunLogFilter.GetFilterMarkers(filterLevel) : null;

        // === expand=jobs: 列出 job 列表(不下载日志,轻量 API 调用) ===
        if (string.Equals(expand, "jobs", StringComparison.OrdinalIgnoreCase)) {
            return await ListJobsAsync(client, owner, repoName, run_id, cancellationToken).ConfigureAwait(false);
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
                var (parsedStep, parsedType) = TryParseSectionType(expandStep);
                expandStep = parsedStep;
                sectionType = parsedType;
            }

            // expand=step:Name/section:Type: 从 Level2 内容缓存读取(ADR 0067)
            if (expandStep is not null && sectionType is not null) {
                return await GetSectionContentAsync(owner, repoName, run_id, job_id, expandStep, sectionType, working_dir, wantRefresh, filterLevel, maxLines, skip, cancellationToken).ConfigureAwait(false);
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
            if (expandStep is not null)
                return BuildStepSectionResult(summary, expandStep, run_id);
        }

        // === 常规模式: log=false 看详情, log=true 拉日志 ===
        var wantLog = log == true;

        if (wantLog) {
            // log=true: 用 REST API 日志流 + 过滤/分页
            return await StreamAndFilterAsync(owner, repoName, run_id, job_id, false, "日志", markers, filterLevel, maxLines, cancellationToken, GitHubRunLogHints.LogHint, skip).ConfigureAwait(false);
        }

        // log=false: 获取 run 详情,根据 verbosity 选择输出格式
        var detailPath = attempt is not null
            ? $"repos/{owner}/{repoName}/actions/runs/{run_id}/attempts/{attempt}"
            : $"repos/{owner}/{repoName}/actions/runs/{run_id}";
        var detailResult = await client.SendAsync(HttpMethod.Get, detailPath, ct: cancellationToken).ConfigureAwait(false);
        if (!detailResult.Success) return Fail(detailResult.Error);

        // 优化D1: 默认模式(verbosity=0/null)run 失败/进行中时,附加失败 job 列表+"尚未拉取"提示
        // 渐进式披露: 首次 view 就置顶错误,AI 无需再调 expand=jobs→expand=failed 两步
        if (verbosity is null or 0)
            return Ok(await EnhanceDefaultViewWithJobsAsync(client, owner, repoName, run_id, detailResult.Body, cancellationToken).ConfigureAwait(false));

        return verbosity switch {
            2 => Ok(detailResult.Body),
            1 => Ok(FilterJsonFields(detailResult.Body, "id,name,head_branch,head_sha,status,conclusion,run_number,event,created_at,updated_at,html_url,display_title")),
            _ => Ok(GitHubRunViewSummarizer.SummarizeRunView(detailResult.Body))
        };
    }

    /// <summary>
    /// 优化D1: 默认模式增强 — run 失败/进行中时,并行调 jobs API 附加失败 job 列表+"尚未拉取"提示
    /// <para>渐进式披露: 首次 view 就置顶错误,AI 无需再调 expand=jobs→expand=failed 两步</para>
    /// <para>run 成功时不附加,保持简洁;verbosity=2/1 不附加(保持原始 JSON/精简格式)</para>
    /// </summary>
    private async Task<string> EnhanceDefaultViewWithJobsAsync(
        IGitHubApiClient client, string owner, string repo, string runId,
        string runDetailJson, CancellationToken ct) {
        var baseSummary = GitHubRunViewSummarizer.SummarizeRunView(runDetailJson);

        string? status, conclusion;
        try {
            var runDetail = JsonSerializer.Deserialize(runDetailJson, GitHubApiJsonContext.Safe.RunDetailResponse);
            status = runDetail?.Status;
            conclusion = runDetail?.Conclusion;
        } catch (JsonException ex) {
            _logger?.LogWarning(ex, "解析 run 详情 JSON 失败(status/conclusion),返回基础摘要");
            return baseSummary;
        }

        // 成功的 run 不附加(保持简洁)
        var isFailed = conclusion is "failure" or "cancelled" or "timed_out";
        var isInProgress = string.Equals(status, "in_progress", StringComparison.OrdinalIgnoreCase);
        if (!isFailed && !isInProgress) return baseSummary;

        // 调 jobs API 获取 job 列表(轻量,不下载日志)
        var jobsResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}/jobs", paginate: true, ct: ct).ConfigureAwait(false);
        if (!jobsResult.Success) return baseSummary;

        var jobs = new List<(long id, string name, string status, string conclusion)>();
        try {
            var jobsResp = JsonSerializer.Deserialize(jobsResult.Body, GitHubApiJsonContext.Safe.RunJobListResponse);
            if (jobsResp is null) return baseSummary;
            foreach (var job in jobsResp.Jobs) {
                var name = string.IsNullOrEmpty(job.Name) ? "unknown" : job.Name;
                var s = string.IsNullOrEmpty(job.Status) ? "?" : job.Status;
                var c = job.Conclusion ?? "";
                jobs.Add((job.Id, name, s, c));
            }
        } catch (JsonException ex) {
            _logger?.LogWarning(ex, "解析 jobs 列表 JSON 失败,返回基础摘要");
            return baseSummary;
        }

        if (jobs.Count == 0) return baseSummary;

        var failedJobs = jobs.Where(j => j.conclusion == "failure").ToList();
        var cancelledJobs = jobs.Where(j => j.conclusion == "cancelled").ToList();
        var inProgressJobs = jobs.Where(j => j.status == "in_progress").ToList();
        // 无异常 job 时不需要附加(全部成功但 run 还在 in_progress 的情况已由 isInProgress 拦截)
        if (failedJobs.Count == 0 && cancelledJobs.Count == 0 && inProgressJobs.Count == 0) return baseSummary;

        var sb = new StringBuilder(baseSummary);
        sb.Append('\n').Append('\n');

        if (failedJobs.Count > 0) {
            sb.Append($"❌ 失败 Job ({failedJobs.Count} 个):\n");
            foreach (var (id, name, _, _) in failedJobs)
                sb.Append($"  ❌ {id,15}  {name}\n");
        }
        if (cancelledJobs.Count > 0) {
            sb.Append($"⊘ 取消 Job ({cancelledJobs.Count} 个):\n");
            foreach (var (id, name, _, _) in cancelledJobs)
                sb.Append($"  ⊘ {id,15}  {name}\n");
        }
        if (inProgressJobs.Count > 0) {
            sb.Append($"⏳ 进行中 Job ({inProgressJobs.Count} 个):\n");
            foreach (var (id, name, _, _) in inProgressJobs)
                sb.Append($"  ⏳ {id,15}  {name}\n");
        }

        sb.Append("\n📋 日志尚未拉取(按需阅读):\n");
        if (failedJobs.Count > 0)
            sb.Append("  expand=failed     → 直接拉失败步骤日志(量少,推荐)\n");
        sb.Append("  expand=jobs       → 查看全部 job 列表\n");
        if (failedJobs.Count > 0)
            sb.Append($"  expand=steps job_id={failedJobs[0].id}  → 查看指定 job 步骤\n");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 解析 expandStep 中的 /section:Type 后缀 — 返回 (不含后缀的 expandStep, sectionType)
    /// </summary>
    private ToolResult BuildStepSectionResult(RunLogSummary summary, string expandStep, string run_id) {
        if (!summary.SectionCounts.TryGetValue(expandStep, out var secs))
            return Ok($"未找到步骤 '{expandStep}'，可用步骤: {string.Join(", ", summary.SectionCounts.Keys)}");
        var summaryText = secs
            .OrderBy(kvp => GitHubRunLogFilter.SectionOrder(kvp.Key))
            .Select(kvp => $"  {kvp.Key,-8} {kvp.Value,5} 行  (用 expand=step:{expandStep}/section:{kvp.Key} 查看)");
        return Ok(string.Join('\n', summaryText) + GitHubRunLogHints.SectionHint, $"Run {run_id} 步骤:{expandStep} sections({secs.Count} 类):");
    }

    private static (string? ExpandStep, string? SectionType) TryParseSectionType(string expandStep) {
        var sectionIdx = expandStep.IndexOf("/section:", StringComparison.OrdinalIgnoreCase);
        if (sectionIdx < 0) return (expandStep, null);
        var sectionType = expandStep[(sectionIdx + 9)..].Trim();
        return (expandStep[..sectionIdx].Trim(), sectionType);
    }

    /// <summary>
    /// 获取指定步骤 section 的日志内容 — 从 Level2 内容缓存读取,应用过滤/分页/提示
    /// </summary>
    private async Task<ToolResult> GetSectionContentAsync(
        string owner, string repo, string runId, string? jobId, string expandStep, string sectionType,
        string? workingDir, bool wantRefresh, GitHubLogFilter filterLevel, int maxLines, int skip, CancellationToken ct) {
        var sectionLines = await GetOrFetchSectionAsync(owner, repo, runId, jobId, expandStep, sectionType, workingDir, wantRefresh, ct).ConfigureAwait(false);
        if (sectionLines is null)
            return Ok($"未找到步骤 '{expandStep}' 或 section '{sectionType}'，建议先 expand=step:{expandStep} 查看 section 摘要");

        var (secText, secHasMore) = GitHubRunLogFilter.SkipAndTruncate(sectionLines, maxLines, skip);
        if (secHasMore)
            secText += GitHubRunLogHints.TruncatedHint;
        if (GitHubRunLogFilter.HasNoStackTrace(secText))
            secText += GitHubRunLogHints.NoStackTraceHint;
        var secPrefix = GitHubRunLogFilter.BuildPrefix(runId, $"步骤:{expandStep}/section:{sectionType}", filterLevel, sectionLines.Count);
        return Ok(secText, secPrefix);
    }

    /// <summary>
    /// 列出 Run 下的所有 job(不下载日志,轻量 API 调用) — 诱导式 drill-down 第一步
    /// <para>返回 job ID/名称/状态/结论,AI 选择目标 job 后用 expand=steps job_id=xxx 按需下载</para>
    /// </summary>
    private Task<ToolResult> ListJobsAsync(IGitHubApiClient client, string owner, string repo, string runId, CancellationToken ct)
        => _logFetcher.ListJobsAsync(client, owner, repo, runId, ct);


    /// <summary>
    /// 从 Level1 摘要缓存获取或流式拉取 — 三级缓存: MemoryCache → 文件级缓存(.jcc/gh_cache/) → 下载
    /// <para>文件级缓存跨进程共享,updatedAt 验证检测 rerun 脏数据,Actor 管道异步写入不阻塞</para>
    /// <para>ADR 0067 两级缓存 + 文件级持久化: 摘要(轻量)+内容(大量行)按 section 独立缓存</para>
    /// </summary>
    private Task<RunLogSummary?> GetOrFetchSummaryAsync(string owner, string repo, string runId, string? jobId, string? workingDir, bool refresh, CancellationToken ct)
        => LogCacheService.GetOrFetchSummaryAsync(owner, repo, runId, jobId, workingDir, refresh, ct);

    /// <summary>
    /// 从 Level2 内容缓存获取指定 section 的日志行 — MemoryCache → 触发 Level1 填充 → 文件 raw 补填 → 再读
    /// <para>内存压力时 Level2 可被独立驱逐,下次访问时通过 Level1 触发从 .raw 文件重新解析填充</para>
    /// <para>Bug 修复: Level1 MemoryCache 命中时不填充 Level2,需从文件缓存 raw 补填</para>
    /// </summary>
    private Task<List<string>?> GetOrFetchSectionAsync(
        string owner, string repo, string runId, string? jobId, string stepName, string sectionType,
        string? workingDir, bool refresh, CancellationToken ct)
        => LogCacheService.GetOrFetchSectionAsync(owner, repo, runId, jobId, stepName, sectionType, workingDir, refresh, ct);

    /// <summary>
    /// 从 GitHub REST API 获取 Run 的 updated_at — 用于检测 rerun 后日志是否更新
    /// <para>轻量 API 调用(不下载日志),&lt; 1s</para>
    /// </summary>
    private Task<string?> FetchUpdatedAtAsync(string owner, string repo, string runId, CancellationToken ct)
        => LogCacheService.FetchUpdatedAtAsync(owner, repo, runId, ct);



    /// <summary>
    /// 获取指定 Run 中所有失败 job 的日志 — 逐行 yield(合并多个 job 日志)
    /// <para>用于 expand=failed 模式,只拉 conclusion=failure 的 job 日志</para>
    /// </summary>
    private IAsyncEnumerable<string> GetFailedJobLogsAsync(
        string owner, string repo, string runId,
        CancellationToken ct)
        => LogFilterRunner.GetFailedJobLogsAsync(owner, repo, runId, ct);
    /// <summary>
    /// 智能过滤测试失败行 — 状态机提取 Failed + Error Message + Stack Trace,Rust 风格输出
    /// <para>状态机: Normal → InFailedTest(遇到 Failed/[FAIL]) → InErrorMessage(Error Message:) → InStackTrace(Stack Trace:) → Normal</para>
    /// <para>输出: 每个失败测试用 --> line N 指示, | 管道符标注日志行, = 总结行</para>
    /// </summary>
    private Task<ToolResult> FilterFailedTestsAsync(
        string owner, string repo, string runId, string? jobId,
        int maxLines, int skipLines, CancellationToken ct)
        => LogFilterRunner.FilterFailedTestsAsync(owner, repo, runId, jobId, maxLines, skipLines, ct);

    /// <summary>
    /// 流式拉取 + 过滤 + 分页跳过(不缓存,用于 --log-failed 或一次性过滤)
    /// <para>日志源: failedOnly=true → 失败 job 日志; jobId 有值 → 单 job 日志; 否则 → 整个 run 日志</para>
    /// </summary>
    private Task<ToolResult> StreamAndFilterAsync(
        string owner, string repo, string runId, string? jobId, bool failedOnly,
        string scope, FrozenSet<string>? markers, GitHubLogFilter? filterLevel,
        int maxLines, CancellationToken ct, string? hint = null, int skipLines = 0)
        => LogFilterRunner.StreamAndFilterAsync(owner, repo, runId, jobId, failedOnly, scope, markers, filterLevel, maxLines, ct, hint, skipLines);

    /// <summary>
    /// 重跑 Actions Run — 默认只重跑失败的 job，支持 debug 日志和指定 job 重跑，调 REST API POST rerun-failed-jobs/rerun-jobs/rerun
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunRerun, "重跑 Actions Run(默认只重跑失败的 job,支持 debug 日志和指定 job)", "github")]
    public async Task<ToolResult> GhRunRerunAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("是否只重跑失败的 job(默认 true)", Required = false)] bool? failed_only = null,
        [McpToolParameter("启用 debug 日志(可选)", Required = false)] bool? debug = null,
        [McpToolParameter("指定重跑的 job ID(可选,逗号分隔多个,设置后只重跑这些 job)", Required = false)] string? job = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var path = "";
            var body = "{}";
            if (!string.IsNullOrWhiteSpace(job)) {
                path = $"repos/{owner}/{repoName}/actions/runs/{run_id}/rerun-jobs";
                var jobIds = GitHubRunLogFilter.ParseJobIds(job);
                jobIds.RemoveAll(id => id <= 0);
                body = JsonSerializer.Serialize(new RunRerunJobsRequest { JobIds = jobIds, EnableDebugLogging = debug == true ? true : null }, GitHubApiJsonContext.Safe.RunRerunJobsRequest);
            } else if (failed_only != false) {
                path = $"repos/{owner}/{repoName}/actions/runs/{run_id}/rerun-failed-jobs";
                body = debug == true ? """{"enable_debug_logging":true}""" : "{}";
            } else {
                path = $"repos/{owner}/{repoName}/actions/runs/{run_id}/rerun";
                body = debug == true ? """{"enable_debug_logging":true}""" : "{}";
            }
            var result = await client.SendAsync(HttpMethod.Post, path, body, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已重跑 Run {run_id}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 取消 Actions Run — 调 REST API POST cancel
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunCancel, "取消 Actions Run", "github")]
    public async Task<ToolResult> GhRunCancelAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/actions/runs/{run_id}/cancel", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已取消 Run {run_id}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 等待 Actions Run 完成 — 指数退避轮询直到 status==completed,通过 onProgress 报告进度,完成才返回唤醒 LLM
    /// <para>替代 LLM 的 sleep+gh_run_view 轮询模式:工具内部阻塞,一次往返拿到最终结果</para>
    /// <para>信号模型:轮询发现 completed 触发返回(唤醒 LLM),非 sleep 固定等待</para>
    /// <para>指数退避:初始 5s ×1.5 每次,上限 60s,默认超时 30 分钟</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunWait, "等待 Actions Run 完成(指数退避轮询+进度回调,完成才返回)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunWaitAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("超时秒数(默认 1800=30分钟)", Required = false)] int? timeout_seconds = null,
        [McpToolParameter("初始轮询间隔秒数(默认 5,指数退避×1.5上限60s)", Required = false)] int? poll_interval_seconds = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, (client, owner, repoName)
            => GhRunWaitCoreAsync(client, owner, repoName, run_id, timeout_seconds, poll_interval_seconds, working_dir, cancellationToken, onProgress)).ConfigureAwait(false);

    /// <summary>
    /// GhRunWait 核心逻辑 — 指数退避轮询直到 completed,失败时下载日志到磁盘
    /// </summary>
    private async Task<ToolResult> GhRunWaitCoreAsync(IGitHubApiClient client, string owner, string repoName, string runId, int? timeoutSeconds, int? pollIntervalSeconds, string? workingDir, CancellationToken ct, ToolProgressCallback? onProgress) {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds ?? 1800, 1, 7200));
        var initialInterval = TimeSpan.FromSeconds(Math.Clamp(pollIntervalSeconds ?? 5, 1, 60));
        var waitResult = await GitHubRunPoller.WaitForRunCompletionAsync(
            client, owner, repoName, runId, timeout, initialInterval,
            onProgress, "gh_run_wait", ct).ConfigureAwait(false);
        if (waitResult.Outcome == RunWaitOutcome.Error)
            return Fail(waitResult.Error ?? "轮询失败");
        if (waitResult.Outcome == RunWaitOutcome.Timeout)
            return Ok(
                BuildRunWaitSummary(waitResult.Body!, runId, waitResult.PollCount, waitResult.ElapsedMs),
                $"⚠ Run {runId} 等待超时({timeout.TotalSeconds:F0}s),当前状态: {waitResult.Conclusion}。用 gh_run_view {runId} 手动查看,或增大 timeout_seconds");
        var conclusion = waitResult.Conclusion ?? "unknown";
        var summary = BuildRunWaitSummary(waitResult.Body!, runId, waitResult.PollCount, waitResult.ElapsedMs);
        if (!IsFailedConclusion(conclusion))
            return Ok(summary, $"Run {runId} 已完成: {conclusion}");
        var logPath = await DownloadFailedLogsToDiskAsync(owner, repoName, runId, workingDir, ct).ConfigureAwait(false);
        return logPath is not null
            ? Ok(summary + $"\n\n📄 失败 job 日志已下载到:\n{logPath}\n\n💡 用 read 工具读取此文件查看错误详情", $"Run {runId} 已完成: ❌ {conclusion}")
            : Ok(summary + $"\n\n⚠ 失败 job 日志下载失败,用 gh run view {runId} --log --filter failed 手动查看", $"Run {runId} 已完成: ❌ {conclusion}");
    }

    /// <summary>
    /// 判断 conclusion 是否为失败(failure/cancelled/timed_out)
    /// </summary>
    private static bool IsFailedConclusion(string conclusion)
        => conclusion is "failure" or "cancelled" or "timed_out";

    /// <summary>
    /// 下载失败 job 日志到磁盘 — 流式拉取所有 conclusion=failure 的 job 日志,写到 .jcc/gh_logs/run_{id}_{timestamp}.log
    /// <para>复用 GetFailedJobLogsAsync 并行下载多个失败 job(Actor 邮箱模型合并)</para>
    /// <para>只返回磁盘路径,不把日志内容塞进 ToolResult,节约 LLM 上下文</para>
    /// </summary>
    private async Task<string?> DownloadFailedLogsToDiskAsync(
        string owner, string repo, string runId, string? workingDir, CancellationToken ct) {
        if (_logFilterRunner is null) return null;

        var baseDir = string.IsNullOrWhiteSpace(workingDir) ? _fs.GetCurrentDirectory() : workingDir;
        var logDir = _fs.CombinePath(baseDir, ".jcc", "gh_logs");
        if (!_fs.DirectoryExists(logDir)) _fs.CreateDirectory(logDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var logPath = _fs.CombinePath(logDir, $"run_{runId}_{timestamp}.log");

        var sb = new StringBuilder();
        var lineCount = 0;
        await foreach (var line in _logFilterRunner.GetFailedJobLogsAsync(owner, repo, runId, ct).ConfigureAwait(false)) {
            sb.AppendLine(line);
            lineCount++;
        }

        if (lineCount == 0) return null;
        await _fs.WriteAllTextAsync(logPath, sb.ToString(), ct).ConfigureAwait(false);
        return logPath;
    }

    /// <summary>
    /// 构建 Run 等待结果摘要 — 包含 conclusion、耗时、轮询次数、run 详情关键字段
    /// </summary>
    private static string BuildRunWaitSummary(string runJson, string runId, int pollCount, long elapsedMs) {
        var sb = new StringBuilder(256);
        sb.AppendLine($"轮询次数: {pollCount}, 耗时: {elapsedMs / 1000}s");
        try {
            var run = JsonSerializer.Deserialize(runJson, GitHubApiJsonContext.Safe.RunDetailResponse);
            var conclusion = run?.Conclusion ?? "unknown";
            var htmlUrl = run?.HtmlUrl ?? "";
            var displayConclusion = conclusion switch {
                "success" => "✅ success",
                "failure" => "❌ failure",
                "cancelled" => "🚫 cancelled",
                "timed_out" => "⏱ timed_out",
                "neutral" => "➖ neutral",
                _ => conclusion
            };
            sb.AppendLine($"结论: {displayConclusion}");
            if (!string.IsNullOrEmpty(htmlUrl)) sb.Append($"URL: {htmlUrl}");
        } catch (JsonException) {
            sb.Append(runJson);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 下载 Run artifact — 列出 artifacts 后下载匹配的 zip 文件，支持名称过滤
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunDownload, "下载 Run artifact(zip 文件,支持名称过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunDownloadAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("保存目录", Required = true)] string dir,
        [McpToolParameter("artifact 名称过滤(可选,支持 * 通配,默认下载全部)", Required = false)] string? name = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var artifactsResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/runs/{run_id}/artifacts", ct: cancellationToken).ConfigureAwait(false);
            if (!artifactsResult.Success) return Fail(artifactsResult.Error);

            List<(string artifactName, long artifactId)> artifacts;
            try {
                var artsResp = JsonSerializer.Deserialize(artifactsResult.Body, GitHubApiJsonContext.Safe.RunArtifactListResponse);
                artifacts = [];
                if (artsResp is not null) {
                    foreach (var art in artsResp.Artifacts) {
                        if (string.IsNullOrEmpty(art.Name) || art.Id == 0) continue;
                        if (!string.IsNullOrWhiteSpace(name) && !SimpleMatchArtifact(name, art.Name)) continue;
                        artifacts.Add((art.Name, art.Id));
                    }
                }
            } catch (Exception ex) { return Fail($"解析 artifact 列表失败: {ex.Message}"); }

            if (artifacts.Count == 0) return Fail($"Run {run_id} 没有匹配的 artifact{(string.IsNullOrWhiteSpace(name) ? "" : $" (name={name})")}");

            _fs.CreateDirectory(dir);

            using var semaphore = new SemaphoreSlim(4, 4);
            var downloadTasks = artifacts.Select(async art => {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try {
                    var filePath = _fs.CombinePath(dir, $"{art.artifactName}.zip");
                    try {
                        var dlResult = await client.DownloadArtifactAsync(owner, repoName, art.artifactId, filePath, cancellationToken).ConfigureAwait(false);
                        return (art.artifactName, Success: dlResult.Success, Error: dlResult.Error);
                    } catch (Exception ex) {
                        return (art.artifactName, Success: false, Error: ex.Message);
                    }
                } finally {
                    semaphore.Release();
                }
            }).ToArray();

            var dlResults = await Task.WhenAll(downloadTasks).ConfigureAwait(false);

            var sb = new StringBuilder();
            var successCount = 0;
            var failCount = 0;
            foreach (var (artName, success, error) in dlResults) {
                if (success) {
                    successCount++;
                    sb.AppendLine($"[OK] {artName}.zip");
                } else {
                    failCount++;
                    sb.AppendLine($"[FAIL] {artName}: {error}");
                }
            }
            sb.AppendLine();
            sb.Append($"汇总: {successCount} 成功, {failCount} 失败, 共 {artifacts.Count} 个 artifact");
            return failCount == 0 ? Ok(sb.ToString(), $"Run {run_id} artifact 下载完成:") : Fail(sb.ToString());
        }).ConfigureAwait(false);

    /// <summary>
    /// 简单通配符匹配 — 支持 * 通配（用于 artifact 名称过滤）
    /// </summary>
    private static bool SimpleMatchArtifact(string pattern, string name) {
        if (string.IsNullOrEmpty(pattern)) return true;
        if (pattern == "*") return true;
        if (!pattern.Contains('*')) return name == pattern;
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(name, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// 删除 Actions Run — 调 REST API DELETE
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunDelete, "删除 Actions Run", "github")]
    public async Task<ToolResult> GhRunDeleteAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/actions/runs/{run_id}", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已删除 Run {run_id}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 观察 Run 直到完成 — 轮询 GET /actions/runs/{id} 直到 status=completed，显示进度
    /// <para>--compact 只显示相关/失败步骤；--exit-status 失败时返回错误；--interval 刷新间隔(默认 3 秒)</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRunWatch, "观察 Run 直到完成(轮询进度)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunWatchAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("刷新间隔秒数(默认 3)", Required = false)] int? interval = null,
        [McpToolParameter("compact=true 只显示相关/失败步骤", Required = false)] bool? compact = null,
        [McpToolParameter("exit_status=true 失败时返回错误", Required = false)] bool? exit_status = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var intervalSec = interval is > 0 ? interval.Value : 3;
            var sb = new StringBuilder(512);
            string? finalStatus = null;
            string? finalConclusion = null;
            while (true) {
                var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/runs/{run_id}", ct: cancellationToken).ConfigureAwait(false);
                if (!result.Success) return Fail(result.Error);
                try {
                    var run = JsonSerializer.Deserialize(result.Body, GitHubApiJsonContext.Safe.RunDetailResponse);
                    var status = run?.Status ?? "";
                    var conclusion = run?.Conclusion ?? "";
                    var displayTitle = run?.DisplayTitle ?? "";
                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {status}{(string.IsNullOrEmpty(conclusion) ? "" : $" / {conclusion}")} — {displayTitle}");
                    if (status == "completed") { finalStatus = status; finalConclusion = conclusion; break; }
                } catch (JsonException) {
                    return Fail($"解析 Run 响应失败: {result.Body[..Math.Min(200, result.Body.Length)]}");
                }
                try { await Task.Delay(intervalSec * 1000, cancellationToken).ConfigureAwait(false); } catch (TaskCanceledException) { return Ok(sb.ToString(), "Run watch 已取消"); }
            }
            var failed = finalConclusion == "failure";
            if (failed && exit_status == true) return Fail(sb.ToString());
            return Ok(sb.ToString(), $"Run {run_id} 已完成: {finalConclusion}");
        }).ConfigureAwait(false);
}