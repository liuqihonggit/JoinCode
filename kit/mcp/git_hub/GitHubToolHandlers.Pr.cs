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
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(pr_number);
            var cacheKey = BuildGhCacheKey("gh_pr_view", $"{owner}/{repoName}/{number}");
            return await GetOrFetchWithCacheAsync(client, cacheKey, $"repos/{owner}/{repoName}/pulls/{number}", verbose, SummarizePr, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

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
    /// 查看 PR diff — 调 REST API 获取 PR 的 diff_url 后下载 patch 文本
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrDiff, "查看 PR diff(patch 文本)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrDiffAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
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
            return diffResult.Success ? Ok(diffResult.Body) : Fail(diffResult.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 查看 PR 的 CI 检查状态 — 调 REST API 获取 check-runs，正确处理 skipping 语义（非失败）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrChecks, "查看 PR 的 CI 检查状态(pass/fail/pending/skipping,skipping 非失败)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrChecksAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, (client, owner, repoName)
            => GhPrChecksCoreAsync(client, owner, repoName, pr_number, cancellationToken)).ConfigureAwait(false);

    /// <summary>
    /// GhPrChecks 核心逻辑 — 调 REST API 获取 check-runs,正确处理 skipping 语义(非失败)
    /// </summary>
    private async Task<ToolResult> GhPrChecksCoreAsync(IGitHubApiClient client, string owner, string repoName, string prNumber, CancellationToken ct) {
        var number = ParseNumberFromRef(prNumber);
        var prResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls/{number}", ct: ct).ConfigureAwait(false);
        if (!prResult.Success) return Fail(prResult.Error);
        string? headSha;
        try {
            using var doc = JsonDocument.Parse(prResult.Body);
            headSha = doc.RootElement.GetProperty("head").GetProperty("sha").GetString();
        } catch (Exception ex) { return Fail($"解析 PR head sha 失败: {ex.Message}"); }
        if (string.IsNullOrEmpty(headSha)) return Fail("无法从 PR 响应中解析 head.sha");
        var checksResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/commits/{headSha}/check-runs", ct: ct).ConfigureAwait(false);
        if (!checksResult.Success) return Fail(checksResult.Error);
        var sb = new StringBuilder();
        var passCount = 0; var failCount = 0; var pendingCount = 0; var skipCount = 0;
        try {
            using var doc = JsonDocument.Parse(checksResult.Body);
            foreach (var run in doc.RootElement.GetProperty("check_runs").EnumerateArray()) {
                var name = run.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
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
        return Ok(sb.ToString());
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
    /// 合并 PR — 支持 squash/merge/rebase 方式和 auto-merge（CI 通过后自动合并），可选删除分支
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrMerge, "合并 PR(支持 squash/merge/rebase + auto-merge)", "github")]
    public async Task<ToolResult> GhPrMergeAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("合并方式(squash/merge/rebase,默认 squash)", Required = false)] string? merge_method = null,
        [McpToolParameter("是否启用 auto-merge(CI 通过后自动合并)", Required = false)] bool? auto_merge = null,
        [McpToolParameter("合并后是否删除分支", Required = false)] bool? delete_branch = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, (client, owner, repoName)
            => GhPrMergeCoreAsync(client, owner, repoName, pr_number, merge_method, auto_merge, delete_branch, cancellationToken)).ConfigureAwait(false);

    /// <summary>
    /// GhPrMerge 核心逻辑 — 支持 squash/merge/rebase + auto-merge,可选删除分支
    /// </summary>
    private async Task<ToolResult> GhPrMergeCoreAsync(IGitHubApiClient client, string owner, string repoName, string prNumber, string? mergeMethod, bool? autoMerge, bool? deleteBranch, CancellationToken ct) {
        var number = ParseNumberFromRef(prNumber);
        var method = string.IsNullOrWhiteSpace(mergeMethod) ? "squash" : mergeMethod;
        if (autoMerge == true) {
            var prResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls/{number}", ct: ct).ConfigureAwait(false);
            if (!prResult.Success) return Fail(prResult.Error);
            string? nodeId;
            try {
                using var doc = JsonDocument.Parse(prResult.Body);
                nodeId = doc.RootElement.GetProperty("node_id").GetString();
            } catch (Exception ex) { return Fail($"解析 PR node_id 失败: {ex.Message}"); }
            if (string.IsNullOrEmpty(nodeId)) return Fail("无法从 PR 响应中解析 node_id");
            var graphqlMethod = method.ToUpperInvariant() switch { "SQUASH" => "SQUASH", "REBASE" => "REBASE", _ => "MERGE" };
            var graphqlBody = $$"""{"query":"mutation { enablePullRequestAutoMerge(input: {pullRequestId: \"{{nodeId}}\", mergeMethod: {{graphqlMethod}}}) { pullRequest { number } } }"}""";
            var graphqlResult = await client.SendAsync(HttpMethod.Post, "graphql", graphqlBody, ct: ct).ConfigureAwait(false);
            if (!graphqlResult.Success) return Fail(graphqlResult.Error);
            return Ok($"已为 PR {number} 启用 auto-merge（{method}）");
        }
        var body = $$"""{"merge_method":"{{method}}"}""";
        var result = await client.SendAsync(HttpMethod.Put, $"repos/{owner}/{repoName}/pulls/{number}/merge", body, ct: ct).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        if (deleteBranch == true) {
            await TryDeleteBranchAsync(client, owner, repoName, number, ct).ConfigureAwait(false);
        }
        return OkBrief(result.Body, "PR 合并成功");
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
    /// 检出 PR 分支到本地 — 走 git fetch + checkout，分支名格式 pr-{number}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrCheckout, "检出 PR 分支到本地", "github")]
    public async Task<ToolResult> GhPrCheckoutAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_git is null) return Fail("git 命令执行器未配置（IGitCommandRunner 未注入）");
        var number = ParseNumberFromRef(pr_number);
        var branchName = $"pr-{number}";

        var fetchResult = await _git.ExecuteAsync($"fetch origin pull/{number}/head:{branchName}", working_dir, cancellationToken).ConfigureAwait(false);
        if (!fetchResult.Success) return Fail(fetchResult.Error);

        var checkoutResult = await _git.ExecuteAsync($"checkout {branchName}", working_dir, cancellationToken).ConfigureAwait(false);
        return checkoutResult.Success ? Ok(checkoutResult.Output, $"已检出 PR {number}") : Fail(checkoutResult.Error);
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
                var commentBody = $$"""{"body":{{JsonEscapeString(comment)}}}""";
                await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", commentBody, ct: cancellationToken).ConfigureAwait(false);
            }
            var body = """{"state":"closed"}""";
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
                var commentBody = $$"""{"body":{{JsonEscapeString(comment)}}}""";
                await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", commentBody, ct: cancellationToken).ConfigureAwait(false);
            }
            var body = """{"state":"open"}""";
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/pulls/{number}", body, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已重开 PR {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 创建 PR — 支持 title/head/base/body/draft，调 REST API POST
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhPrCreate, "创建 PR(支持 title/head/base/body/draft)", "github")]
    public async Task<ToolResult> GhPrCreateAsync(
        [McpToolParameter("PR 标题", Required = true)] string title,
        [McpToolParameter("源分支(head)", Required = true)] string head,
        [McpToolParameter("目标分支(base,默认 main)", Required = false)] string? @base = null,
        [McpToolParameter("PR 正文(可选,支持 markdown)", Required = false)] string? body = null,
        [McpToolParameter("是否 draft PR(可选,默认 false)", Required = false)] bool? draft = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var jsonBody = BuildPrCreateJson(title, head, @base, body, draft);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/pulls", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, "PR 创建成功") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 构建 PR 创建 JSON 请求体 — 流式构建器(AOT 友好,无手拼 StringBuilder)
    /// </summary>
    private static string BuildPrCreateJson(string title, string head, string? @base, string? body, bool? draft)
        => new GitHubJsonObjectBuilder()
            .String("title", title)
            .String("head", head)
            .StringIf("base", @base)
            .StringIf("body", body)
            .BoolIfTrue("draft", draft)
            .Build();

    /// <summary>
    /// JSON 字符串转义 — 委托 GitHubJsonObjectBuilder.EscapeString(保留供单字段 JSON 如 {"body":...} 使用)
    /// </summary>
    private static string JsonEscapeString(string value) => GitHubJsonObjectBuilder.EscapeString(value);
}