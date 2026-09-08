namespace McpToolDispatch;

/// <summary>
/// GitHub PR 工具 — 直调 GitHub REST API（ADR 0073），替代原 gh pr 子命令包装
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhPrView, "查看 PR 详情(号/标题/状态/URL/body/变更统计)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrViewAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选,默认当前目录)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;
        var number = ParsePrNumber(pr_number);

        var result = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls/{number}", ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body) : Fail(result.Error);
    }

    [McpTool(GitHubToolNameConstants.GhPrList, "列出 PR(支持状态/数量/作者过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrListAsync(
        [McpToolParameter("状态(open/closed/merged/all,默认 open)", Required = false)] string? state = null,
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("作者过滤(可选)", Required = false)] string? author = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var query = new Dictionary<string, string> { ["state"] = string.IsNullOrWhiteSpace(state) ? "open" : state, ["per_page"] = (limit ?? 30).ToString() };
        if (!string.IsNullOrWhiteSpace(author)) query["creator"] = author;

        var result = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls", query: query, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body) : Fail(result.Error);
    }

    [McpTool(GitHubToolNameConstants.GhPrDiff, "查看 PR diff(patch 文本)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrDiffAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;
        var number = ParsePrNumber(pr_number);

        var prResult = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls/{number}", ct: cancellationToken).ConfigureAwait(false);
        if (!prResult.Success) return Fail(prResult.Error);

        string? diffUrl;
        try
        {
            using var doc = JsonDocument.Parse(prResult.Body);
            diffUrl = doc.RootElement.TryGetProperty("diff_url", out var diffEl) ? diffEl.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "解析 PR diff_url 失败");
            diffUrl = null;
        }

        if (string.IsNullOrEmpty(diffUrl)) return Fail("无法从 PR 响应中解析 diff_url");

        var diffResult = await _apiClient.SendAsync(HttpMethod.Get, diffUrl, ct: cancellationToken).ConfigureAwait(false);
        return diffResult.Success ? Ok(diffResult.Body) : Fail(diffResult.Error);
    }

    [McpTool(GitHubToolNameConstants.GhPrChecks, "查看 PR 的 CI 检查状态(pass/fail/pending/skipping,skipping 非失败)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrChecksAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;
        var number = ParsePrNumber(pr_number);

        var prResult = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls/{number}", ct: cancellationToken).ConfigureAwait(false);
        if (!prResult.Success) return Fail(prResult.Error);

        string? headSha;
        try
        {
            using var doc = JsonDocument.Parse(prResult.Body);
            headSha = doc.RootElement.GetProperty("head").GetProperty("sha").GetString();
        }
        catch (Exception ex) { return Fail($"解析 PR head sha 失败: {ex.Message}"); }

        if (string.IsNullOrEmpty(headSha)) return Fail("无法从 PR 响应中解析 head.sha");

        var checksResult = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/commits/{headSha}/check-runs", ct: cancellationToken).ConfigureAwait(false);
        if (!checksResult.Success) return Fail(checksResult.Error);

        var sb = new StringBuilder();
        var passCount = 0; var failCount = 0; var pendingCount = 0; var skipCount = 0;
        try
        {
            using var doc = JsonDocument.Parse(checksResult.Body);
            foreach (var run in doc.RootElement.GetProperty("check_runs").EnumerateArray())
            {
                var name = run.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var status = run.TryGetProperty("conclusion", out var conclEl) ? conclEl.GetString() ?? "pending" : "pending";
                var displayStatus = status switch
                {
                    "success" => "pass",
                    "failure" or "cancelled" or "timed_out" => "fail",
                    "skipped" or "neutral" => "skipping",
                    _ => "pending"
                };
                sb.AppendLine($"{name}\t{displayStatus}");
                switch (displayStatus) { case "pass": passCount++; break; case "fail": failCount++; break; case "pending": pendingCount++; break; case "skipping": skipCount++; break; }
            }
        }
        catch (Exception ex) { return Fail($"解析 check-runs 失败: {ex.Message}"); }

        sb.AppendLine();
        sb.Append($"汇总: {passCount} 通过, {failCount} 失败, {pendingCount} 进行中, {skipCount} 跳过(依赖链跳过,非失败)");
        return Ok(sb.ToString());
    }

    [McpTool(GitHubToolNameConstants.GhPrMerge, "合并 PR(支持 squash/merge/rebase + auto-merge)", "github")]
    public async Task<ToolResult> GhPrMergeAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("合并方式(squash/merge/rebase,默认 squash)", Required = false)] string? merge_method = null,
        [McpToolParameter("是否启用 auto-merge(CI 通过后自动合并)", Required = false)] bool? auto_merge = null,
        [McpToolParameter("合并后是否删除分支", Required = false)] bool? delete_branch = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;
        var number = ParsePrNumber(pr_number);
        var method = string.IsNullOrWhiteSpace(merge_method) ? "squash" : merge_method;

        if (auto_merge == true)
        {
            var enableBody = $$"""{"merge_method":"{{method}}"}""";
            var enableResult = await _apiClient.SendAsync(HttpMethod.Put, $"repos/{owner}/{repoName}/pulls/{number}/enable-automerge", enableBody, ct: cancellationToken).ConfigureAwait(false);
            if (!enableResult.Success) return Fail(enableResult.Error);
            return Ok($"已为 PR {number} 启用 auto-merge（{method}）");
        }

        var body = $$"""{"merge_method":"{{method}}"}""";
        var result = await _apiClient.SendAsync(HttpMethod.Put, $"repos/{owner}/{repoName}/pulls/{number}/merge", body, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);

        if (delete_branch == true)
        {
            var prResult = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/pulls/{number}", ct: cancellationToken).ConfigureAwait(false);
            if (prResult.Success)
            {
                try
                {
                    using var doc = JsonDocument.Parse(prResult.Body);
                    var branchName = doc.RootElement.GetProperty("head").GetProperty("ref").GetString();
                    if (!string.IsNullOrEmpty(branchName)) await _apiClient.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/git/refs/heads/{branchName}", ct: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) { _logger?.LogDebug(ex, "删除 PR 分支失败(非致命)"); }
            }
        }
        return Ok(result.Body, "PR 合并成功");
    }

    [McpTool(GitHubToolNameConstants.GhPrCheckout, "检出 PR 分支到本地", "github")]
    public async Task<ToolResult> GhPrCheckoutAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_git is null) return Fail("git 命令执行器未配置（IGitCommandRunner 未注入）");
        var number = ParsePrNumber(pr_number);
        var branchName = $"pr-{number}";

        var fetchResult = await _git.ExecuteAsync($"fetch origin pull/{number}/head:{branchName}", working_dir, cancellationToken).ConfigureAwait(false);
        if (!fetchResult.Success) return Fail(fetchResult.Error);

        var checkoutResult = await _git.ExecuteAsync($"checkout {branchName}", working_dir, cancellationToken).ConfigureAwait(false);
        return checkoutResult.Success ? Ok(checkoutResult.Output, $"已检出 PR {number}") : Fail(checkoutResult.Error);
    }

    [McpTool(GitHubToolNameConstants.GhPrClose, "关闭 PR(可附评论)", "github")]
    public async Task<ToolResult> GhPrCloseAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("关闭评论(可选)", Required = false)] string? comment = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;
        var number = ParsePrNumber(pr_number);

        if (!string.IsNullOrWhiteSpace(comment))
        {
            var commentBody = $$"""{"body":{{JsonEscapeString(comment)}}}""";
            await _apiClient.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", commentBody, ct: cancellationToken).ConfigureAwait(false);
        }

        var body = """{"state":"closed"}""";
        var result = await _apiClient.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/pulls/{number}", body, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body, $"已关闭 PR {number}") : Fail(result.Error);
    }

    [McpTool(GitHubToolNameConstants.GhPrReopen, "重新打开 PR", "github")]
    public async Task<ToolResult> GhPrReopenAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;
        var number = ParsePrNumber(pr_number);

        var body = """{"state":"open"}""";
        var result = await _apiClient.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/pulls/{number}", body, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body, $"已重开 PR {number}") : Fail(result.Error);
    }

    [McpTool(GitHubToolNameConstants.GhPrCreate, "创建 PR(支持 title/head/base/body/draft)", "github")]
    public async Task<ToolResult> GhPrCreateAsync(
        [McpToolParameter("PR 标题", Required = true)] string title,
        [McpToolParameter("源分支(head)", Required = true)] string head,
        [McpToolParameter("目标分支(base,默认 main)", Required = false)] string? @base = null,
        [McpToolParameter("PR 正文(可选,支持 markdown)", Required = false)] string? body = null,
        [McpToolParameter("是否 draft PR(可选,默认 false)", Required = false)] bool? draft = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var jsonBody = BuildPrCreateJson(title, head, @base, body, draft);
        var result = await _apiClient.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/pulls", jsonBody, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body, "PR 创建成功") : Fail(result.Error);
    }

    /// <summary>
    /// 构建 PR 创建 JSON 请求体 — 手动拼接避免 JsonSerializer 序列化开销(AOT 友好)
    /// </summary>
    private static string BuildPrCreateJson(string title, string head, string? @base, string? body, bool? draft)
    {
        var sb = new StringBuilder(256);
        sb.Append("""{"title":""");
        sb.Append(JsonEscapeString(title));
        sb.Append(""","head":""");
        sb.Append(JsonEscapeString(head));
        sb.Append('"');
        if (!string.IsNullOrWhiteSpace(@base))
        {
            sb.Append(""","base":""");
            sb.Append(JsonEscapeString(@base));
            sb.Append('"');
        }
        if (!string.IsNullOrWhiteSpace(body))
        {
            sb.Append(""","body":""");
            sb.Append(JsonEscapeString(body));
            sb.Append('"');
        }
        if (draft == true)
            sb.Append(""","draft":true""");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// 从 PR 编号或 URL 提取数字编号
    /// </summary>
    private static string ParsePrNumber(string prNumber)
    {
        if (string.IsNullOrEmpty(prNumber)) return prNumber;
        var lastSlash = prNumber.LastIndexOf('/');
        if (lastSlash < 0) return prNumber;
        return prNumber[(lastSlash + 1)..];
    }

    /// <summary>
    /// JSON 字符串转义（AOT 友好，替代 JsonSerializer.Serialize）
    /// </summary>
    private static string JsonEscapeString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:X4}");
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static ToolResult ApiClientNotConfigured() =>
        ToolResultBuilder.Error().WithText("GitHub REST API 客户端未配置（IGitHubApiClient 未注入）").Build();

    private static ToolResult RepoNotResolved() =>
        ToolResultBuilder.Error().WithText("无法解析仓库 owner/repo（请传 repo 参数或确保当前目录是 GitHub 仓库）").Build();
}
