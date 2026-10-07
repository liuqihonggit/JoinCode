namespace McpToolDispatch;

/// <summary>
/// GitHub PR 工具 — 直调 GitHub REST API（ADR 0073），替代原 gh pr 子命令包装
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 查看 PR 详情 — 调 REST API 获取 PR 信息，verbose=true 返回完整 JSON（从缓存读），默认精简输出
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrView, "查看 PR 详情(号/标题/状态/URL/body/变更统计)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrViewAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选,默认当前目录)", Required = false)] string? working_dir = null,
        [McpToolParameter("verbose=true 返回完整 JSON(从缓存读,不调 API); 默认 false 精简输出(调 API 更新缓存)", Required = false)] bool? verbose = null,
        [McpToolParameter("comments=true 附带评论列表", Required = false)] bool? comments = null,
        [McpToolParameter("web=true 只返回 PR 浏览器 URL", Required = false)] bool? web = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(pr_number);
            var apiPath = $"repos/{owner}/{repoName}/pulls/{number}";
            if (web == true) {
                var prResult = await client.SendAsync(HttpMethod.Get, apiPath, ct: cancellationToken).ConfigureAwait(false);
                if (!prResult.Success) return Fail(prResult.Error);
                var url = ExtractHtmlUrl(prResult.Body);
                return string.IsNullOrEmpty(url) ? Fail("无法从 PR 响应中解析 html_url") : Ok(url);
            }
            if (comments == true) {
                var prResult = await client.SendAsync(HttpMethod.Get, apiPath, ct: cancellationToken).ConfigureAwait(false);
                if (!prResult.Success) return Fail(prResult.Error);
                var summary = verbose == true ? prResult.Body : SummarizePr(prResult.Body);
                var commentsResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/issues/{number}/comments", ct: cancellationToken).ConfigureAwait(false);
                if (!commentsResult.Success) return Fail(commentsResult.Error);
                return Ok($"{summary}\n\n## 评论\n{SummarizeComments(commentsResult.Body)}");
            }
            var cacheKey = BuildGhCacheKey("gh_pr_view", $"{owner}/{repoName}/{number}");
            return await GetOrFetchWithCacheAsync(client, cacheKey, apiPath, verbose, SummarizePr, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

    /// <summary>
    /// 从 PR JSON 响应提取 html_url 字段 — 用于 web=true 模式
    /// </summary>
    private static string? ExtractHtmlUrl(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("html_url", out var el) ? el.GetString() : null;
        } catch {
            return null;
        }
    }

    /// <summary>
    /// 精简评论列表 JSON — 提取每条评论的作者和正文，格式: - @login: body
    /// </summary>
    private static string SummarizeComments(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder(256);
            foreach (var item in doc.RootElement.EnumerateArray()) {
                var body = item.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                var login = item.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
                sb.AppendLine($"- @{login}: {body}");
            }
            return sb.ToString();
        } catch {
            return json;
        }
    }

    /// <summary>
    /// 列出 PR — 支持状态/数量/作者/标签/指派人/分支/draft/搜索过滤，表格格式输出
    /// <para>简单过滤(state/base/head)走 pulls API；复杂过滤(label/assignee/draft/search/author)走 search API</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrList, "列出 PR(支持状态/数量/作者/标签/指派人/分支/draft/搜索过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrListAsync(
        [McpToolParameter("状态(open/closed/merged/all,默认 open)", Required = false)] string? state = null,
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("作者过滤(可选)", Required = false)] string? author = null,
        [McpToolParameter("标签过滤(可选,多个用逗号)", Required = false)] string? label = null,
        [McpToolParameter("指派人过滤(可选)", Required = false)] string? assignee = null,
        [McpToolParameter("目标分支过滤(可选)", Required = false)] string? @base = null,
        [McpToolParameter("源分支过滤(可选)", Required = false)] string? head = null,
        [McpToolParameter("是否 draft PR(可选)", Required = false)] bool? draft = null,
        [McpToolParameter("搜索查询(可选,GitHub search 语法)", Required = false)] string? search = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var pageCount = (limit ?? 30).ToString();
            var stateVal = string.IsNullOrWhiteSpace(state) ? "open" : state;
            var needSearch = NeedSearchApi(label, assignee, draft, search, author);
            if (needSearch) {
                var q = BuildPrSearchQuery(owner, repoName, stateVal, author, label, assignee, @base, head, draft, search);
                var query = new Dictionary<string, string> { ["q"] = q, ["per_page"] = pageCount };
                var result = await client.SendAsync(HttpMethod.Get, "search/issues", query: query, ct: cancellationToken).ConfigureAwait(false);
                if (!result.Success) return Fail(result.Error);
                return Ok(SummarizePrList(result.Body));
            }
            var pullsQuery = new Dictionary<string, string> { ["state"] = stateVal, ["per_page"] = pageCount };
            if (!string.IsNullOrWhiteSpace(@base)) pullsQuery["base"] = @base;
            if (!string.IsNullOrWhiteSpace(head)) pullsQuery["head"] = head;
            var pullsResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls", query: pullsQuery, ct: cancellationToken).ConfigureAwait(false);
            if (!pullsResult.Success) return Fail(pullsResult.Error);
            return Ok(SummarizePrList(pullsResult.Body));
        }).ConfigureAwait(false);

    /// <summary>
    /// 判断是否需要走 search API — 有 label/assignee/draft/search/author 任一参数即走 search
    /// </summary>
    private static bool NeedSearchApi(string? label, string? assignee, bool? draft, string? search, string? author)
        => !string.IsNullOrWhiteSpace(label) || !string.IsNullOrWhiteSpace(assignee) || draft == true || !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(author);

    /// <summary>
    /// 构建 PR search API 查询字符串 — is:pr repo:{owner}/{repo} + 各过滤条件
    /// </summary>
    private static string BuildPrSearchQuery(string owner, string repo, string state, string? author, string? label, string? assignee, string? @base, string? head, bool? draft, string? search) {
        var parts = new List<string> { "is:pr", $"repo:{owner}/{repo}", $"state:{state}" };
        if (!string.IsNullOrWhiteSpace(author)) parts.Add($"author:{author}");
        if (!string.IsNullOrWhiteSpace(assignee)) parts.Add($"assignee:{assignee}");
        if (!string.IsNullOrWhiteSpace(label)) foreach (var l in label.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) parts.Add($"label:{l}");
        if (draft == true) parts.Add("draft:true");
        if (!string.IsNullOrWhiteSpace(@base)) parts.Add($"base:{@base}");
        if (!string.IsNullOrWhiteSpace(head)) parts.Add($"head:{head}");
        if (!string.IsNullOrWhiteSpace(search)) parts.Add(search);
        return string.Join(" ", parts);
    }

    /// <summary>
    /// 查看 PR diff — 调 REST API 获取 PR 的 diff_url 后下载 patch 文本，支持 name-only/exclude 过滤
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrDiff, "查看 PR diff(patch 文本,支持 name-only/exclude 过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrDiffAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("只显示文件名(可选)", Required = false)] bool? name_only = null,
        [McpToolParameter("排除文件(可选,glob 模式,多个用逗号)", Required = false)] string? exclude = null,
        [McpToolParameter("patch 格式(可选,默认即 patch)", Required = false)] bool? patch = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(pr_number);
            var prResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls/{number}", ct: cancellationToken).ConfigureAwait(false);
            if (!prResult.Success) return Fail(prResult.Error);
            string? diffUrl;
            try {
                using var doc = JsonDocument.Parse(prResult.Body);
                diffUrl = doc.RootElement.TryGetProperty("diff_url", out var diffEl) ? diffEl.GetString() : null;
            } catch (Exception ex) {
                _logger?.LogDebug(ex, "解析 PR diff_url 失败");
                diffUrl = null;
            }
            if (string.IsNullOrEmpty(diffUrl)) return Fail("无法从 PR 响应中解析 diff_url");
            var diffResult = await client.SendAsync(HttpMethod.Get, diffUrl, ct: cancellationToken).ConfigureAwait(false);
            if (!diffResult.Success) return Fail(diffResult.Error);
            var diffText = diffResult.Body;
            if (!string.IsNullOrWhiteSpace(exclude)) diffText = FilterDiffByExclude(diffText, exclude!);
            if (name_only == true) diffText = ExtractDiffFileNames(diffText);
            return Ok(diffText);
        }).ConfigureAwait(false);

    /// <summary>
    /// 从 diff 文本提取文件名列表 — 解析每段 diff --git a/path b/path 头
    /// </summary>
    private static string ExtractDiffFileNames(string diffText) {
        var sb = new StringBuilder(256);
        foreach (var line in diffText.Split('\n')) {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal)) {
                var filePath = ExtractFilePathFromDiffHeader(line);
                if (filePath is not null) sb.AppendLine(filePath);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 从 diff --git a/path b/path 头提取文件路径
    /// </summary>
    private static string? ExtractFilePathFromDiffHeader(string line) {
        var rest = line.AsSpan("diff --git ".Length);
        var spaceIdx = rest.IndexOf(' ');
        if (spaceIdx > 0) {
            var path = rest.Slice(spaceIdx + 1);
            if (path.StartsWith("b/")) path = path.Slice(2);
            return path.ToString();
        }
        return null;
    }

    /// <summary>
    /// 按 glob 模式过滤 diff 段 — 排除文件名匹配任一模式的段
    /// </summary>
    private static string FilterDiffByExclude(string diffText, string excludePatterns) {
        var patterns = excludePatterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var segments = new List<(string FileName, string Content)>();
        var currentSb = new StringBuilder();
        string? currentFile = null;
        foreach (var line in diffText.Split('\n')) {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal)) {
                if (currentSb.Length > 0 && currentFile is not null) segments.Add((currentFile, currentSb.ToString()));
                currentSb = new StringBuilder();
                currentFile = ExtractFilePathFromDiffHeader(line);
            }
            currentSb.AppendLine(line);
        }
        if (currentSb.Length > 0 && currentFile is not null) segments.Add((currentFile, currentSb.ToString()));
        var result = new StringBuilder();
        foreach (var (fileName, content) in segments) {
            if (!patterns.Any(p => MatchesGlob(fileName, p))) result.Append(content);
        }
        return result.ToString();
    }

    /// <summary>
    /// 简单 glob 匹配 — * 匹配任意字符序列, ? 匹配单字符(O(n) 无 GC)
    /// </summary>
    private static bool MatchesGlob(string path, string pattern) {
        int p = 0, g = 0, starP = -1, starG = -1;
        while (p < path.Length) {
            if (g < pattern.Length && (pattern[g] == path[p] || pattern[g] == '?')) { p++; g++; }
            else if (g < pattern.Length && pattern[g] == '*') { starG = g; starP = p; g++; }
            else if (starG != -1) { g = starG + 1; p = starP + 1; starP++; }
            else return false;
        }
        while (g < pattern.Length && pattern[g] == '*') g++;
        return g == pattern.Length;
    }

    /// <summary>
    /// 查看 PR 的 CI 检查状态 — 调 REST API 获取 check-runs，正确处理 skipping 语义（非失败），支持 required 过滤/watch 轮询/fail-fast
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrChecks, "查看 PR 的 CI 检查状态(支持 required 过滤/watch 轮询/fail-fast)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrChecksAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("只显示 required checks(可选)", Required = false)] bool? required = null,
        [McpToolParameter("watch 模式轮询直到完成(可选)", Required = false)] bool? watch = null,
        [McpToolParameter("轮询间隔秒数(可选,默认 10)", Required = false)] int? interval = null,
        [McpToolParameter("有失败立即标记(可选)", Required = false)] bool? fail_fast = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (watch == true) return await GhPrChecksWatchAsync(client, owner, repoName, pr_number, interval, fail_fast, required, cancellationToken).ConfigureAwait(false);
            return await GhPrChecksCoreAsync(client, owner, repoName, pr_number, fail_fast, required, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

    /// <summary>
    /// GhPrChecks watch 模式 — 轮询 check-runs 直到全部 completed，用 interval 间隔
    /// </summary>
    private async Task<ToolResult> GhPrChecksWatchAsync(IGitHubApiClient client, string owner, string repoName, string prNumber, int? intervalSec, bool? failFast, bool? required, CancellationToken ct) {
        var delay = TimeSpan.FromSeconds(Math.Clamp(intervalSec ?? 10, 1, 300));
        for (var i = 0; i < 120; i++) {
            var checkResult = await GhPrChecksCoreAsync(client, owner, repoName, prNumber, failFast, required, ct).ConfigureAwait(false);
            if (checkResult.IsError) return checkResult;
            var text = checkResult.GetFirstText() ?? "";
            if (!text.Contains("进行中")) return checkResult;
            if (failFast == true && text.Contains("失败")) return checkResult;
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
        return Ok("轮询超时，仍有 checks 进行中");
    }

    /// <summary>
    /// GhPrChecks 核心逻辑 — 调 REST API 获取 check-runs,正确处理 skipping 语义(非失败),支持 required 过滤和 fail-fast 标记
    /// </summary>
    private async Task<ToolResult> GhPrChecksCoreAsync(IGitHubApiClient client, string owner, string repoName, string prNumber, bool? failFast, bool? required, CancellationToken ct) {
        var number = ParseNumberFromRef(prNumber);
        var prResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls/{number}", ct: ct).ConfigureAwait(false);
        if (!prResult.Success) return Fail(prResult.Error);
        string? headSha; string? headRef;
        try {
            using var doc = JsonDocument.Parse(prResult.Body);
            var head = doc.RootElement.GetProperty("head");
            headSha = head.GetProperty("sha").GetString();
            headRef = head.TryGetProperty("ref", out var refEl) ? refEl.GetString() : null;
        } catch (Exception ex) { return Fail($"解析 PR head sha 失败: {ex.Message}"); }
        if (string.IsNullOrEmpty(headSha)) return Fail("无法从 PR 响应中解析 head.sha");
        var checksResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/commits/{headSha}/check-runs", ct: ct).ConfigureAwait(false);
        if (!checksResult.Success) return Fail(checksResult.Error);
        var requiredContexts = required == true && !string.IsNullOrEmpty(headRef)
            ? await GetRequiredStatusChecksAsync(client, owner, repoName, headRef!, ct).ConfigureAwait(false)
            : null;
        var sb = new StringBuilder();
        var passCount = 0; var failCount = 0; var pendingCount = 0; var skipCount = 0;
        try {
            using var doc = JsonDocument.Parse(checksResult.Body);
            foreach (var run in doc.RootElement.GetProperty("check_runs").EnumerateArray()) {
                var name = run.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                if (requiredContexts is not null && !requiredContexts.Contains(name)) continue;
                var status = run.TryGetProperty("conclusion", out var conclEl) ? conclEl.GetString() ?? "pending" : "pending";
                var displayStatus = status switch {
                    "success" => "pass",
                    "failure" or "cancelled" or "timed_out" => "fail",
                    "skipped" or "neutral" => "skipping",
                    _ => "pending"
                };
                sb.AppendLine($"{name}\t{displayStatus}");
                switch (displayStatus) { case "pass": passCount++; break; case "fail": failCount++; break; case "pending": pendingCount++; break; case "skipping": skipCount++; break; }
            }
        } catch (Exception ex) { return Fail($"解析 check-runs 失败: {ex.Message}"); }
        sb.AppendLine();
        sb.Append($"汇总: {passCount} 通过, {failCount} 失败, {pendingCount} 进行中, {skipCount} 跳过(依赖链跳过,非失败)");
        if (failFast == true && failCount > 0) sb.Append("\n⚠ fail-fast: 检测到失败");
        return Ok(sb.ToString());
    }

    /// <summary>
    /// 获取分支保护规则的 required_status_checks — required 过滤用，保护规则不存在(404)时返回 null(降级显示全部)
    /// </summary>
    private async Task<HashSet<string>?> GetRequiredStatusChecksAsync(IGitHubApiClient client, string owner, string repo, string branch, CancellationToken ct) {
        var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/branches/{branch}/protection/required_status_checks", ct: ct).ConfigureAwait(false);
        if (!result.Success) return null;
        try {
            using var doc = JsonDocument.Parse(result.Body);
            if (doc.RootElement.TryGetProperty("contexts", out var contextsEl) && contextsEl.ValueKind == JsonValueKind.Array) {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var c in contextsEl.EnumerateArray()) if (c.ValueKind == JsonValueKind.String) set.Add(c.GetString()!);
                return set;
            }
        } catch (Exception ex) { _logger?.LogDebug(ex, "解析 required_status_checks 失败"); }
        return null;
    }

    /// <summary>
    /// 等待 PR 所有 CI checks 完成 — 指数退避轮询 check-runs 直到全部 completed,通过 onProgress 报告进度
    /// <para>替代 LLM 的 sleep+gh_pr_checks 轮询模式:工具内部阻塞,一次往返拿到最终结果</para>
    /// <para>信号模型:轮询发现全部 completed 触发返回(唤醒 LLM)</para>
    /// <para>指数退避:初始 5s ×1.5 每次,上限 60s,默认超时 30 分钟</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrWait, "等待 PR 所有 CI checks 完成(指数退避轮询+进度回调,完成才返回)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrWaitAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("超时秒数(默认 1800=30分钟)", Required = false)] int? timeout_seconds = null,
        [McpToolParameter("初始轮询间隔秒数(默认 5,指数退避×1.5上限60s)", Required = false)] int? poll_interval_seconds = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, (client, owner, repoName)
            => GhPrWaitCoreAsync(client, owner, repoName, pr_number, timeout_seconds, poll_interval_seconds, working_dir, cancellationToken, onProgress)).ConfigureAwait(false);

    /// <summary>
    /// GhPrWait 核心逻辑 — 指数退避轮询 check-runs 直到全部 completed,失败时下载日志到磁盘
    /// </summary>
    private async Task<ToolResult> GhPrWaitCoreAsync(IGitHubApiClient client, string owner, string repoName, string prNumber, int? timeoutSeconds, int? pollIntervalSeconds, string? workingDir, CancellationToken ct, ToolProgressCallback? onProgress) {
        var number = ParseNumberFromRef(prNumber);
        var prResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls/{number}", ct: ct).ConfigureAwait(false);
        if (!prResult.Success) return Fail(prResult.Error);
        string? headSha;
        try {
            using var doc = JsonDocument.Parse(prResult.Body);
            headSha = doc.RootElement.GetProperty("head").GetProperty("sha").GetString();
        } catch (Exception ex) { return Fail($"解析 PR head sha 失败: {ex.Message}"); }
        if (string.IsNullOrEmpty(headSha)) return Fail("无法从 PR 响应中解析 head.sha");
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds ?? 1800, 1, 7200));
        var initialInterval = TimeSpan.FromSeconds(Math.Clamp(pollIntervalSeconds ?? 5, 1, 60));
        var waitResult = await GitHubRunPoller.WaitForPrChecksCompletionAsync(
            client, owner, repoName, headSha, number, timeout, initialInterval,
            onProgress, "gh_pr_wait", ct).ConfigureAwait(false);
        if (waitResult.Outcome == RunWaitOutcome.Error)
            return Fail(waitResult.Error ?? "轮询失败");
        if (waitResult.Outcome == RunWaitOutcome.Timeout)
            return Ok(
                $"汇总: {waitResult.Summary}\n轮询次数: {waitResult.PollCount}, 耗时: {waitResult.ElapsedMs / 1000}s",
                $"⚠ PR #{number} 等待超时({timeout.TotalSeconds:F0}s),仍有 checks 进行中。用 gh_pr_checks {number} 手动查看,或增大 timeout_seconds");
        var summaryText = $"汇总: {waitResult.Summary}\n轮询次数: {waitResult.PollCount}, 耗时: {waitResult.ElapsedMs / 1000}s";
        if (waitResult.FailCount == 0)
            return Ok(summaryText, $"PR #{number} 所有 CI checks 已完成 ✅");
        var logPaths = await DownloadFailedPrRunsLogsToDiskAsync(client, owner, repoName, headSha, workingDir, ct).ConfigureAwait(false);
        return logPaths.Count > 0
            ? Ok(summaryText + "\n\n📄 失败 job 日志已下载到:\n" + string.Join("\n", logPaths) + "\n\n💡 用 read 工具读取这些文件查看错误详情", $"PR #{number} CI checks 已完成(有 {waitResult.FailCount} 个失败) ❌")
            : Ok(summaryText + $"\n\n⚠ 未找到可下载的 Actions run 日志(可能是第三方 CI),用 gh pr checks {number} 查看失败 check 名称", $"PR #{number} CI checks 已完成(有 {waitResult.FailCount} 个失败) ❌");
    }

    /// <summary>
    /// 下载 PR 对应失败 Actions run 的日志到磁盘 — 用 head_sha 查 actions/runs,对每个失败 run 下载失败 job 日志
    /// <para>每个失败 run 生成独立日志文件 .jcc/gh_logs/run_{runId}_{timestamp}.log</para>
    /// </summary>
    private async Task<List<string>> DownloadFailedPrRunsLogsToDiskAsync(
        IGitHubApiClient client, string owner, string repo, string headSha, string? workingDir, CancellationToken ct) {
        var runsResult = await client.SendAsync(
            HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs",
            query: new Dictionary<string, string> { ["head_sha"] = headSha },
            ct: ct).ConfigureAwait(false);
        if (!runsResult.Success) return [];

        List<string> failedRunIds;
        try {
            using var doc = JsonDocument.Parse(runsResult.Body);
            failedRunIds = doc.RootElement.TryGetProperty("workflow_runs", out var runsEl)
                ? runsEl.EnumerateArray()
                    .Where(r => r.TryGetProperty("conclusion", out var c)
                        && c.ValueKind == JsonValueKind.String
                        && c.GetString() == "failure")
                    .Select(r => r.TryGetProperty("id", out var idEl) ? idEl.GetRawText() : "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList()
                : [];
        } catch {
            return [];
        }

        var paths = new List<string>(failedRunIds.Count);
        foreach (var runId in failedRunIds) {
            var path = await DownloadFailedLogsToDiskAsync(owner, repo, runId, workingDir, ct).ConfigureAwait(false);
            if (path is not null) paths.Add(path);
        }
        return paths;
    }

    /// <summary>
    /// 合并 PR — 支持 squash/merge/rebase 方式、auto-merge、自定义提交标题/正文、禁用 auto-merge、可选删除分支
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrMerge, "合并 PR(支持 squash/merge/rebase + auto-merge + 自定义提交信息)", "github")]
    public async Task<ToolResult> GhPrMergeAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("合并方式(squash/merge/rebase,默认 squash)", Required = false)] string? merge_method = null,
        [McpToolParameter("是否启用 auto-merge(CI 通过后自动合并)", Required = false)] bool? auto_merge = null,
        [McpToolParameter("禁用 auto-merge(可选)", Required = false)] bool? disable_auto = null,
        [McpToolParameter("合并提交标题(可选)", Required = false)] string? subject = null,
        [McpToolParameter("合并提交正文(可选)", Required = false)] string? body = null,
        [McpToolParameter("管理员强制合并(可选,绕过 required checks)", Required = false)] bool? admin = null,
        [McpToolParameter("合并后是否删除分支", Required = false)] bool? delete_branch = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, (client, owner, repoName)
            => GhPrMergeCoreAsync(client, owner, repoName, pr_number, merge_method, auto_merge, disable_auto, subject, body, delete_branch, cancellationToken)).ConfigureAwait(false);

    /// <summary>
    /// GhPrMerge 核心逻辑 — 支持 squash/merge/rebase + auto-merge/disable-auto + 自定义提交信息 + 可选删除分支
    /// </summary>
    private async Task<ToolResult> GhPrMergeCoreAsync(IGitHubApiClient client, string owner, string repoName, string prNumber, string? mergeMethod, bool? autoMerge, bool? disableAuto, string? subject, string? body, bool? deleteBranch, CancellationToken ct) {
        var number = ParseNumberFromRef(prNumber);
        var method = string.IsNullOrWhiteSpace(mergeMethod) ? "squash" : mergeMethod;
        if (disableAuto == true) {
            var (nodeId, nodeErr) = await GetPrNodeIdAsync(client, owner, repoName, number, ct).ConfigureAwait(false);
            if (nodeErr is not null) return Fail(nodeErr);
            var graphqlBody = $$"""{"query":"mutation { disablePullRequestAutoMerge(input: {pullRequestId: \"{{nodeId}}\"}) { pullRequest { number } } }"}""";
            var graphqlResult = await client.SendAsync(HttpMethod.Post, "graphql", graphqlBody, ct: ct).ConfigureAwait(false);
            if (!graphqlResult.Success) return Fail(graphqlResult.Error);
            return Ok($"已为 PR {number} 禁用 auto-merge");
        }
        if (autoMerge == true) {
            var (nodeId, nodeErr) = await GetPrNodeIdAsync(client, owner, repoName, number, ct).ConfigureAwait(false);
            if (nodeErr is not null) return Fail(nodeErr);
            var graphqlMethod = method.ToUpperInvariant() switch { "SQUASH" => "SQUASH", "REBASE" => "REBASE", _ => "MERGE" };
            var graphqlBody = $$"""{"query":"mutation { enablePullRequestAutoMerge(input: {pullRequestId: \"{{nodeId}}\", mergeMethod: {{graphqlMethod}}}) { pullRequest { number } } }"}""";
            var graphqlResult = await client.SendAsync(HttpMethod.Post, "graphql", graphqlBody, ct: ct).ConfigureAwait(false);
            if (!graphqlResult.Success) return Fail(graphqlResult.Error);
            return Ok($"已为 PR {number} 启用 auto-merge（{method}）");
        }
        var mergeRequest = new PrMergeRequest { MergeMethod = method, CommitTitle = subject, CommitMessage = body };
        var mergeBody = JsonSerializer.Serialize(mergeRequest, GitHubApiJsonContext.Safe.PrMergeRequest);
        var result = await client.SendAsync(HttpMethod.Put, $"repos/{owner}/{repoName}/pulls/{number}/merge", mergeBody, ct: ct).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        if (deleteBranch == true) {
            await TryDeleteBranchAsync(client, owner, repoName, number, ct).ConfigureAwait(false);
        }
        return OkBrief(result.Body, "PR 合并成功");
    }

    /// <summary>
    /// 获取 PR 的 node_id — 用于 GraphQL mutation（auto-merge enable/disable）
    /// </summary>
    private async Task<(string NodeId, string? Error)> GetPrNodeIdAsync(IGitHubApiClient client, string owner, string repoName, string number, CancellationToken ct) {
        var prResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls/{number}", ct: ct).ConfigureAwait(false);
        if (!prResult.Success) return ("", prResult.Error);
        try {
            using var doc = JsonDocument.Parse(prResult.Body);
            var nodeId = doc.RootElement.GetProperty("node_id").GetString();
            if (string.IsNullOrEmpty(nodeId)) return ("", "无法从 PR 响应中解析 node_id");
            return (nodeId, null);
        } catch (Exception ex) { return ("", $"解析 PR node_id 失败: {ex.Message}"); }
    }

    /// <summary>
    /// 尝试删除 PR 分支 — 合并成功后清理远程分支(非致命,失败仅记日志)
    /// </summary>
    private async Task TryDeleteBranchAsync(IGitHubApiClient client, string owner, string repo, string number, CancellationToken ct) {
        var prResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/pulls/{number}", ct: ct).ConfigureAwait(false);
        if (!prResult.Success) return;
        try {
            using var doc = JsonDocument.Parse(prResult.Body);
            var branchName = doc.RootElement.GetProperty("head").GetProperty("ref").GetString();
            if (!string.IsNullOrEmpty(branchName)) await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repo}/git/refs/heads/{branchName}", ct: ct).ConfigureAwait(false);
        } catch (Exception ex) { _logger?.LogDebug(ex, "删除 PR 分支失败(非致命)"); }
    }

    /// <summary>
    /// 检出 PR 分支到本地 — 走 git fetch + checkout，支持自定义分支名/强制/detached HEAD
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrCheckout, "检出 PR 分支到本地(支持自定义分支名/强制/detached HEAD)", "github")]
    public async Task<ToolResult> GhPrCheckoutAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("本地分支名(可选,默认 pr-{number})", Required = false)] string? branch = null,
        [McpToolParameter("强制重置已有分支(可选)", Required = false)] bool? force = null,
        [McpToolParameter("detached HEAD 检出(可选)", Required = false)] bool? detach = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_git is null) return Fail("git 命令执行器未配置（IGitCommandRunner 未注入）");
        var number = ParseNumberFromRef(pr_number);

        if (detach == true) {
            var fetchResult = await _git.ExecuteAsync($"fetch origin pull/{number}/head", working_dir, cancellationToken).ConfigureAwait(false);
            if (!fetchResult.Success) return Fail(fetchResult.Error);
            var checkoutResult = await _git.ExecuteAsync("checkout --detach FETCH_HEAD", working_dir, cancellationToken).ConfigureAwait(false);
            return checkoutResult.Success ? Ok(checkoutResult.Output, $"已检出 PR {number}(detached HEAD)") : Fail(checkoutResult.Error);
        }

        var branchName = string.IsNullOrWhiteSpace(branch) ? $"pr-{number}" : branch;
        var forceArg = force == true ? " --force" : "";
        var fetchResult2 = await _git.ExecuteAsync($"fetch origin pull/{number}/head:{branchName}{forceArg}", working_dir, cancellationToken).ConfigureAwait(false);
        if (!fetchResult2.Success) return Fail(fetchResult2.Error);

        var checkoutResult2 = await _git.ExecuteAsync($"checkout {branchName}", working_dir, cancellationToken).ConfigureAwait(false);
        return checkoutResult2.Success ? Ok(checkoutResult2.Output, $"已检出 PR {number}") : Fail(checkoutResult2.Error);
    }

    /// <summary>
    /// 关闭 PR — 可选附评论和删除分支，调 REST API PATCH state=closed
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrClose, "关闭 PR(可附评论,可选删除分支)", "github")]
    public async Task<ToolResult> GhPrCloseAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("关闭评论(可选)", Required = false)] string? comment = null,
        [McpToolParameter("关闭后是否删除分支", Required = false)] bool? delete_branch = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(pr_number);
            if (!string.IsNullOrWhiteSpace(comment)) {
                var commentBody = JsonSerializer.Serialize(new CommentRequest { Body = comment }, GitHubApiJsonContext.Safe.CommentRequest);
                await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", commentBody, ct: cancellationToken).ConfigureAwait(false);
            }
            var body = JsonSerializer.Serialize(new PrEditRequest { State = "closed" }, GitHubApiJsonContext.Safe.PrEditRequest);
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/pulls/{number}", body, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            if (delete_branch == true) {
                await TryDeleteBranchAsync(client, owner, repoName, number, cancellationToken).ConfigureAwait(false);
            }
            return OkBrief(result.Body, $"已关闭 PR {number}");
        }).ConfigureAwait(false);

    /// <summary>
    /// 重新打开 PR — 可选附评论，调 REST API PATCH state=open
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrReopen, "重新打开 PR(可附评论)", "github")]
    public async Task<ToolResult> GhPrReopenAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("重开评论(可选)", Required = false)] string? comment = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(pr_number);
            if (!string.IsNullOrWhiteSpace(comment)) {
                var commentBody = JsonSerializer.Serialize(new CommentRequest { Body = comment }, GitHubApiJsonContext.Safe.CommentRequest);
                await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", commentBody, ct: cancellationToken).ConfigureAwait(false);
            }
            var body = JsonSerializer.Serialize(new PrEditRequest { State = "open" }, GitHubApiJsonContext.Safe.PrEditRequest);
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/pulls/{number}", body, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已重开 PR {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 创建 PR — 支持 title/head/base/body/draft/assignee/label/reviewer/milestone/body_file/fill，调 REST API POST
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrCreate, "创建 PR(支持 assignee/label/reviewer/milestone/body_file/fill)", "github")]
    public async Task<ToolResult> GhPrCreateAsync(
        [McpToolParameter("PR 标题", Required = true)] string title,
        [McpToolParameter("源分支(head)", Required = true)] string head,
        [McpToolParameter("目标分支(base,默认 main)", Required = false)] string? @base = null,
        [McpToolParameter("PR 正文(可选,支持 markdown)", Required = false)] string? body = null,
        [McpToolParameter("从文件读 body(可选,替代 body)", Required = false)] string? body_file = null,
        [McpToolParameter("是否 draft PR(可选,默认 false)", Required = false)] bool? draft = null,
        [McpToolParameter("指派人(可选,多个用逗号)", Required = false)] string? assignee = null,
        [McpToolParameter("标签(可选,多个用逗号)", Required = false)] string? label = null,
        [McpToolParameter("审阅人(可选,多个用逗号)", Required = false)] string? reviewer = null,
        [McpToolParameter("里程碑 ID(可选)", Required = false)] int? milestone = null,
        [McpToolParameter("从 git commit 自动填充 title/body(可选)", Required = false)] bool? fill = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var actualBody = body;
            if (!string.IsNullOrWhiteSpace(body_file)) actualBody = await _fs.ReadAllTextAsync(body_file, cancellationToken).ConfigureAwait(false);
            var actualTitle = title;
            if (fill == true && _git is not null) {
                var (fillTitle, fillBody) = await GetGitFillAsync(_git, working_dir, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(actualTitle)) actualTitle = fillTitle;
                if (string.IsNullOrWhiteSpace(actualBody)) actualBody = fillBody;
            }
            var jsonBody = BuildPrCreateJson(actualTitle ?? "", head, @base, actualBody, draft);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/pulls", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            var prNumber = ExtractNumberFromResponse(result.Body);
            if (prNumber > 0) await AddPrPostCreateAttributesAsync(client, owner, repoName, prNumber, assignee, label, reviewer, milestone, cancellationToken).ConfigureAwait(false);
            return OkBrief(result.Body, "PR 创建成功");
        }).ConfigureAwait(false);

    /// <summary>
    /// 创建 PR 后添加 assignee/label/reviewer/milestone — 各属性独立调 API，失败不阻断 PR 创建
    /// </summary>
    private static async Task AddPrPostCreateAttributesAsync(IGitHubApiClient client, string owner, string repo, int number, string? assignee, string? label, string? reviewer, int? milestone, CancellationToken ct) {
        if (!string.IsNullOrWhiteSpace(assignee)) {
            var assigneesBody = JsonSerializer.Serialize(new AssigneesRequest { Assignees = ParseCsvToList(assignee) }, GitHubApiJsonContext.Safe.AssigneesRequest);
            await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repo}/issues/{number}/assignees", assigneesBody, ct: ct).ConfigureAwait(false);
        }
        if (!string.IsNullOrWhiteSpace(label)) {
            var labelsArray = JsonSerializer.Serialize(ParseCsvToList(label), GitHubApiJsonContext.Safe.ListString);
            await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repo}/issues/{number}/labels", labelsArray, ct: ct).ConfigureAwait(false);
        }
        if (!string.IsNullOrWhiteSpace(reviewer)) {
            var reviewersBody = JsonSerializer.Serialize(new ReviewersRequest { Reviewers = ParseCsvToList(reviewer) }, GitHubApiJsonContext.Safe.ReviewersRequest);
            await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repo}/pulls/{number}/requested_reviewers", reviewersBody, ct: ct).ConfigureAwait(false);
        }
        if (milestone is not null) {
            var milestoneBody = JsonSerializer.Serialize(new MilestoneRequest { Milestone = milestone }, GitHubApiJsonContext.Safe.MilestoneRequest);
            await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repo}/issues/{number}", milestoneBody, ct: ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 从 PR 创建响应提取 number — 用于后续添加 assignee/label/reviewer
    /// </summary>
    private int ExtractNumberFromResponse(string body) {
        try {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
        } catch (Exception ex) { _logger?.LogDebug(ex, "解析 PR number 失败"); return 0; }
    }

    /// <summary>
    /// 从 git log 获取最新 commit 的 title 和 body — fill 模式用
    /// </summary>
    private async Task<(string Title, string Body)> GetGitFillAsync(IGitCommandRunner git, string? workingDir, CancellationToken ct) {
        var logResult = await git.ExecuteAsync("log -1 --format=%s%n%n%b", workingDir, ct).ConfigureAwait(false);
        if (!logResult.Success) return ("", "");
        var output = logResult.Output;
        var idx = output.IndexOf("\n\n", StringComparison.Ordinal);
        return idx >= 0 ? (output[..idx].Trim(), output[(idx + 2)..]) : (output.Trim(), "");
    }

    /// <summary>
    /// 构建 PR 创建 JSON 请求体 — DTO 序列化(编译期类型安全)
    /// </summary>
    private static string BuildPrCreateJson(string title, string head, string? @base, string? body, bool? draft)
        => JsonSerializer.Serialize(new PrCreateRequest { Title = title, Head = head, Base = @base, Body = body, Draft = draft }, GitHubApiJsonContext.Safe.PrCreateRequest);

    /// <summary>
    /// JSON 字符串转义 — 委托 GitHubJsonObjectBuilder.EscapeString(保留供单字段 JSON 如 {"body":...} 使用)
    /// </summary>
    private static string JsonEscapeString(string value) => GitHubJsonObjectBuilder.EscapeString(value);

    /// <summary>
    /// 评论 PR — 调 REST API POST issues/{number}/comments 端点（PR 复用 issues 评论）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrComment, "评论 PR", "github")]
    public async Task<ToolResult> GhPrCommentAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("评论内容", Required = true)] string body,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(pr_number);
            var reqBody = JsonSerializer.Serialize(new CommentRequest { Body = body }, GitHubApiJsonContext.Safe.CommentRequest);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", reqBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已评论 PR {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 编辑 PR — 修改标题/body/base 分支，调 REST API PATCH
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrEdit, "编辑 PR(title/body/base)", "github")]
    public async Task<ToolResult> GhPrEditAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("新标题(可选)", Required = false)] string? title = null,
        [McpToolParameter("新 body(可选)", Required = false)] string? body = null,
        [McpToolParameter("新 base 分支(可选)", Required = false)] string? @base = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(pr_number);
            var jsonBody = JsonSerializer.Serialize(new PrEditRequest { Title = title, Body = body, Base = @base }, GitHubApiJsonContext.Safe.PrEditRequest);
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/pulls/{number}", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已编辑 PR {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 审查 PR — approve/request_changes/comment，调 REST API POST reviews 端点
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrReview, "审查 PR(approve/request_changes/comment)", "github")]
    public async Task<ToolResult> GhPrReviewAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("审查动作(approve/request_changes/comment)", Required = true)] string action,
        [McpToolParameter("审查评论(可选)", Required = false)] string? body = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(pr_number);
            var eventVal = action.ToLowerInvariant() switch {
                "approve" or "approved" => "APPROVE",
                "request" or "request_changes" or "request_changes" => "REQUEST_CHANGES",
                "comment" => "COMMENT",
                _ => action.ToUpperInvariant(),
            };
            var jsonBody = JsonSerializer.Serialize(new PrReviewRequest { Event = eventVal, Body = body }, GitHubApiJsonContext.Safe.PrReviewRequest);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/pulls/{number}/reviews", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已审查 PR {number}: {eventVal}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 锁定 PR — 调 REST API PUT issues/{number}/lock（PR 复用 issues 锁定机制），可选锁定原因
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrLock, "锁定 PR(可选原因)", "github")]
    public async Task<ToolResult> GhPrLockAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("锁定原因(off-topic/resolved/spam/too heated,可选)", Required = false)] string? reason = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(pr_number);
            var body = string.IsNullOrWhiteSpace(reason) ? null : JsonSerializer.Serialize(new LockRequest { LockReason = reason }, GitHubApiJsonContext.Safe.LockRequest);
            var result = await client.SendAsync(HttpMethod.Put, $"repos/{owner}/{repoName}/issues/{number}/lock", body, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已锁定 PR {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 解锁 PR — 调 REST API DELETE issues/{number}/lock
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrUnlock, "解锁 PR", "github")]
    public async Task<ToolResult> GhPrUnlockAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(pr_number);
            var result = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/issues/{number}/lock", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已解锁 PR {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 查看 PR 状态 — 显示当前仓库 open 状态的 PR 列表(按创建/分配分组)，调 REST API pulls?state=open
    /// <para>系统 gh pr status 显示 3 组(当前分支/你创建的/分配给你的)，需当前用户名；简化为列出 open PR 并按 author 分组</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrStatus, "查看 PR 状态(当前仓库 open PR)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrStatusAsync(
        [McpToolParameter("显示合并冲突状态(可选)", Required = false)] bool? conflict_status = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> { ["state"] = "open", ["per_page"] = "30" };
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls", query: query, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(SummarizePrStatus(result.Body, conflict_status == true));
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简 PR 状态 JSON — 按 author 分组显示 open PR
    /// </summary>
    private static string SummarizePrStatus(string json, bool showConflict) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var byAuthor = new Dictionary<string, List<(int number, string title, string headRef, bool draft, bool mergeable)>>();
            foreach (var pr in doc.RootElement.EnumerateArray()) {
                var number = pr.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
                var title = pr.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var author = pr.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var login) ? login.GetString() ?? "" : "";
                var headRef = pr.TryGetProperty("head", out var h) && h.TryGetProperty("ref", out var hr) ? hr.GetString() ?? "" : "";
                var draft = pr.TryGetProperty("draft", out var d) && d.GetBoolean();
                var mergeable = pr.TryGetProperty("mergeable", out var m) ? (m.ValueKind == JsonValueKind.True) : false;
                if (!byAuthor.TryGetValue(author, out var list)) { list = new(); byAuthor[author] = list; }
                list.Add((number, title, headRef, draft, mergeable));
            }
            var sb = new StringBuilder(512);
            foreach (var (author, prs) in byAuthor) {
                sb.AppendLine($"## {author}");
                foreach (var (number, title, headRef, draft, mergeable) in prs) {
                    var draftMark = draft ? " [draft]" : "";
                    var conflictMark = showConflict && !mergeable ? " [conflict]" : "";
                    sb.AppendLine($"  #{number}: {title}{draftMark}{conflictMark} ({headRef})");
                }
            }
            return sb.ToString();
        } catch {
            return json;
        }
    }

    /// <summary>
    /// 标记 PR 为 ready for review — 调 REST API PATCH pulls/{n} {"draft":false}，--undo 反向设 draft=true
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrReady, "标记 PR 为 ready for review(--undo 转为 draft)", "github")]
    public async Task<ToolResult> GhPrReadyAsync(
        [McpToolParameter("PR 编号或 URL(可选,默认当前分支 PR)", Required = false)] string? pr_number = null,
        [McpToolParameter("undo=true 转为 draft(默认 false 标记 ready)", Required = false)] bool? undo = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (string.IsNullOrWhiteSpace(pr_number)) return Fail("pr ready 需要显式传 pr_number(当前分支自动检测未实现)");
            var number = ParseNumberFromRef(pr_number);
            var draftVal = undo == true ? "true" : "false";
            var body = JsonSerializer.Serialize(new PrDraftRequest { Draft = undo == true }, GitHubApiJsonContext.Safe.PrDraftRequest);
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/pulls/{number}", body, ct: cancellationToken).ConfigureAwait(false);
            var msg = undo == true ? $"已将 PR {number} 转为 draft" : $"已将 PR {number} 标记为 ready for review";
            return result.Success ? OkBrief(result.Body, msg) : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 回退 PR — 获取 PR merge_commit_sha 后用 git revert 创建回退提交，再创建回退 PR
    /// <para>GitHub REST API 无 revert 端点，需本地 git revert + push + create PR</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrRevert, "回退 PR(git revert + 创建回退 PR)", "github")]
    public async Task<ToolResult> GhPrRevertAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("回退 PR 标题(可选)", Required = false)] string? title = null,
        [McpToolParameter("回退 PR body(可选)", Required = false)] string? body = null,
        [McpToolParameter("标记为 draft(可选)", Required = false)] bool? draft = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (_git is null) return Fail("git 命令执行器未配置(IGitCommandRunner 未注入)，pr revert 需要本地 git");
            var number = ParseNumberFromRef(pr_number);
            var prResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls/{number}", ct: cancellationToken).ConfigureAwait(false);
            if (!prResult.Success) return Fail(prResult.Error);
            string? mergeCommitSha;
            string? headRef;
            string? baseRef;
            try {
                using var doc = JsonDocument.Parse(prResult.Body);
                mergeCommitSha = doc.RootElement.TryGetProperty("merge_commit_sha", out var mcs) ? mcs.GetString() : null;
                headRef = doc.RootElement.TryGetProperty("head", out var h) && h.TryGetProperty("ref", out var hr) ? hr.GetString() : null;
                baseRef = doc.RootElement.TryGetProperty("base", out var b) && b.TryGetProperty("ref", out var br) ? br.GetString() : null;
            } catch { mergeCommitSha = null; headRef = null; baseRef = null; }
            if (string.IsNullOrEmpty(mergeCommitSha)) return Fail($"PR {number} 尚未合并，无法 revert");
            var revertBranch = $"revert-{number}-{mergeCommitSha[..7]}";
            var checkoutResult = await _git.ExecuteAsync($"checkout -b {revertBranch} {baseRef}", working_dir, cancellationToken).ConfigureAwait(false);
            if (!checkoutResult.Success) return Fail($"创建回退分支失败: {checkoutResult.Output}");
            var revertResult = await _git.ExecuteAsync($"revert {mergeCommitSha} --no-edit", working_dir, cancellationToken).ConfigureAwait(false);
            if (!revertResult.Success) return Fail($"git revert 失败: {revertResult.Output}");
            var pushResult = await _git.ExecuteAsync($"push origin {revertBranch}", working_dir, cancellationToken).ConfigureAwait(false);
            if (!pushResult.Success) return Fail($"push 失败: {pushResult.Output}");
            var prTitle = string.IsNullOrWhiteSpace(title) ? $"Revert \"{headRef}\"" : title;
            var revertRequest = new PrCreateRequest { Title = prTitle, Head = revertBranch, Base = baseRef ?? "main", Body = body, Draft = draft };
            var jsonBody = JsonSerializer.Serialize(revertRequest, GitHubApiJsonContext.Safe.PrCreateRequest);
            var createResult = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/pulls", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return createResult.Success ? OkBrief(createResult.Body, $"已创建回退 PR(基于 {mergeCommitSha[..7]})") : Fail(createResult.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 更新 PR 分支 — 用 base 分支最新变更更新 PR 分支，调 REST API PUT pulls/{n}/update-branch
    /// <para>update_method: merge(默认) 或 rebase</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrUpdateBranch, "更新 PR 分支(merge/rebase)", "github")]
    public async Task<ToolResult> GhPrUpdateBranchAsync(
        [McpToolParameter("PR 编号或 URL(可选,默认当前分支 PR)", Required = false)] string? pr_number = null,
        [McpToolParameter("rebase=true 用 rebase 更新(默认 merge)", Required = false)] bool? rebase = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (string.IsNullOrWhiteSpace(pr_number)) return Fail("pr update-branch 需要显式传 pr_number(当前分支自动检测未实现)");
            var number = ParseNumberFromRef(pr_number);
            var method = rebase == true ? "rebase" : "merge";
            var body = JsonSerializer.Serialize(new PrUpdateBranchRequest { UpdateMethod = method }, GitHubApiJsonContext.Safe.PrUpdateBranchRequest);
            var result = await client.SendAsync(HttpMethod.Put, $"repos/{owner}/{repoName}/pulls/{number}/update-branch", body, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已更新 PR {number} 分支({method})") : Fail(result.Error);
        }).ConfigureAwait(false);
}