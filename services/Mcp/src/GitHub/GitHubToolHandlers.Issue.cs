namespace McpToolDispatch;

/// <summary>
/// GitHub Issue 工具 — 直调 GitHub REST API（ADR 0073），替代原 gh issue 子命令包装
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhIssueList, "列出 Issue(支持状态/标签/指派人过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhIssueListAsync(
        [McpToolParameter("状态(open/closed/all,默认 open)", Required = false)] string? state = null,
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("标签过滤(可选,多个用逗号)", Required = false)] string? label = null,
        [McpToolParameter("指派人过滤(可选)", Required = false)] string? assignee = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var query = new Dictionary<string, string> { ["state"] = string.IsNullOrWhiteSpace(state) ? "open" : state, ["per_page"] = (limit ?? 30).ToString() };
        if (!string.IsNullOrWhiteSpace(label)) query["labels"] = label;
        if (!string.IsNullOrWhiteSpace(assignee)) query["assignee"] = assignee;

        var result = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/issues", query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeIssueList(result.Body));
    }

    [McpTool(GitHubToolNameConstants.GhIssueView, "查看 Issue 详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhIssueViewAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        [McpToolParameter("verbose=true 返回完整 JSON(从缓存读,不调 API); 默认 false 精简输出(调 API 更新缓存)", Required = false)] bool? verbose = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;
        var number = ParseIssueNumber(issue_number);

        var cacheKey = BuildGhCacheKey("gh_issue_view", $"{owner}/{repoName}/{number}");

        if (verbose == true)
        {
            var cached = TryGetGhCache(cacheKey);
            if (cached is not null)
                return Ok(cached);
        }

        var result = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/issues/{number}", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);

        SaveGhCache(cacheKey, result.Body);
        return Ok(verbose == true ? result.Body : SummarizeIssue(result.Body));
    }

    [McpTool(GitHubToolNameConstants.GhIssueCreate, "创建 Issue(支持标签/指派人)", "github")]
    public async Task<ToolResult> GhIssueCreateAsync(
        [McpToolParameter("Issue 标题", Required = true)] string title,
        [McpToolParameter("Issue 内容(body)", Required = false)] string? body = null,
        [McpToolParameter("标签(可选,多个用逗号)", Required = false)] string? label = null,
        [McpToolParameter("指派人(可选)", Required = false)] string? assignee = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var bodySb = new StringBuilder();
        bodySb.Append('{');
        bodySb.Append("\"title\":" + JsonEscapeString(title));
        if (!string.IsNullOrWhiteSpace(body)) bodySb.Append(",\"body\":" + JsonEscapeString(body));
        if (!string.IsNullOrWhiteSpace(label))
        {
            var labels = label.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var labelArr = new StringBuilder("[");
            for (int i = 0; i < labels.Length; i++)
            {
                if (i > 0) labelArr.Append(',');
                labelArr.Append(JsonEscapeString(labels[i]));
            }
            labelArr.Append(']');
            bodySb.Append(",\"labels\":" + labelArr);
        }
        if (!string.IsNullOrWhiteSpace(assignee)) bodySb.Append(",\"assignees\":[" + JsonEscapeString(assignee) + "]");
        bodySb.Append('}');

        var result = await _apiClient.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues", bodySb.ToString(), ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body, "Issue 创建成功") : Fail(result.Error);
    }

    [McpTool(GitHubToolNameConstants.GhIssueClose, "关闭 Issue(可附评论)", "github")]
    public async Task<ToolResult> GhIssueCloseAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("关闭评论(可选)", Required = false)] string? comment = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;
        var number = ParseIssueNumber(issue_number);

        if (!string.IsNullOrWhiteSpace(comment))
        {
            var commentBody = $$"""{"body":{{JsonEscapeString(comment)}}}""";
            await _apiClient.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", commentBody, ct: cancellationToken).ConfigureAwait(false);
        }

        var body = """{"state":"closed"}""";
        var result = await _apiClient.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/issues/{number}", body, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body, $"已关闭 Issue {number}") : Fail(result.Error);
    }

    [McpTool(GitHubToolNameConstants.GhIssueComment, "评论 Issue", "github")]
    public async Task<ToolResult> GhIssueCommentAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("评论内容", Required = true)] string body,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;
        var number = ParseIssueNumber(issue_number);

        var reqBody = $$"""{"body":{{JsonEscapeString(body)}}}""";
        var result = await _apiClient.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", reqBody, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body, $"已评论 Issue {number}") : Fail(result.Error);
    }

    /// <summary>
    /// 从 Issue 编号或 URL 提取数字编号
    /// </summary>
    private static string ParseIssueNumber(string issueNumber)
    {
        if (string.IsNullOrEmpty(issueNumber)) return issueNumber;
        var lastSlash = issueNumber.LastIndexOf('/');
        if (lastSlash < 0) return issueNumber;
        return issueNumber[(lastSlash + 1)..];
    }
}
