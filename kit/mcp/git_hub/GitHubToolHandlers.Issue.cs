namespace McpToolDispatch;

/// <summary>
/// GitHub Issue 工具 — 直调 GitHub REST API（ADR 0073），替代原 gh issue 子命令包装
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 列出 Issue — 支持状态/标签/指派人/作者/提及/里程碑/搜索/类型过滤，表格格式输出
    /// <para>简单过滤(state/label/assignee/author/mention/milestone)走 issues API；复杂过滤(search/type=pr)走 search API</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueList, "列出 Issue(支持状态/标签/指派人/作者/提及/里程碑/搜索/类型过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhIssueListAsync(
        [McpToolParameter("状态(open/closed/all,默认 open)", Required = false)] string? state = null,
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("标签过滤(可选,多个用逗号)", Required = false)] string? label = null,
        [McpToolParameter("指派人过滤(可选)", Required = false)] string? assignee = null,
        [McpToolParameter("作者过滤(可选)", Required = false)] string? author = null,
        [McpToolParameter("提及过滤(可选)", Required = false)] string? mention = null,
        [McpToolParameter("里程碑过滤(可选,数字 ID 或 * 或 none)", Required = false)] string? milestone = null,
        [McpToolParameter("搜索查询(可选,GitHub search 语法)", Required = false)] string? search = null,
        [McpToolParameter("类型(issue/pr,默认 issue)", Required = false)] string? type = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var pageCount = (limit ?? 30).ToString();
            var stateVal = string.IsNullOrWhiteSpace(state) ? "open" : state;
            var needSearch = !string.IsNullOrWhiteSpace(search) || string.Equals(type, "pr", StringComparison.OrdinalIgnoreCase);
            if (needSearch) {
                var q = BuildIssueSearchQuery(owner, repoName, stateVal, author, mention, milestone, label, assignee, search, type);
                var query = new Dictionary<string, string> { ["q"] = q, ["per_page"] = pageCount };
                var result = await client.SendAsync(HttpMethod.Get, "search/issues", query: query, ct: cancellationToken).ConfigureAwait(false);
                if (!result.Success) return Fail(result.Error);
                return Ok(SummarizeIssueList(result.Body));
            }
            var issuesQuery = new Dictionary<string, string> { ["state"] = stateVal, ["per_page"] = pageCount };
            if (!string.IsNullOrWhiteSpace(label)) issuesQuery["labels"] = label;
            if (!string.IsNullOrWhiteSpace(assignee)) issuesQuery["assignee"] = assignee;
            if (!string.IsNullOrWhiteSpace(author)) issuesQuery["creator"] = author;
            if (!string.IsNullOrWhiteSpace(mention)) issuesQuery["mentioned"] = mention;
            if (!string.IsNullOrWhiteSpace(milestone)) issuesQuery["milestone"] = milestone;
            var issuesResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/issues", query: issuesQuery, ct: cancellationToken).ConfigureAwait(false);
            if (!issuesResult.Success) return Fail(issuesResult.Error);
            return Ok(SummarizeIssueList(issuesResult.Body));
        }).ConfigureAwait(false);

    /// <summary>
    /// 构建 Issue search API 查询字符串 — is:{type} repo:{owner}/{repo} + 各过滤条件
    /// </summary>
    private static string BuildIssueSearchQuery(string owner, string repo, string state, string? author, string? mention, string? milestone, string? label, string? assignee, string? search, string? type) {
        var typeVal = string.Equals(type, "pr", StringComparison.OrdinalIgnoreCase) ? "pr" : "issue";
        var parts = new List<string> { $"is:{typeVal}", $"repo:{owner}/{repo}", $"state:{state}" };
        if (!string.IsNullOrWhiteSpace(author)) parts.Add($"author:{author}");
        if (!string.IsNullOrWhiteSpace(mention)) parts.Add($"mentions:{mention}");
        if (!string.IsNullOrWhiteSpace(milestone)) parts.Add($"milestone:{milestone}");
        if (!string.IsNullOrWhiteSpace(label)) foreach (var l in label.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) parts.Add($"label:{l}");
        if (!string.IsNullOrWhiteSpace(assignee)) parts.Add($"assignee:{assignee}");
        if (!string.IsNullOrWhiteSpace(search)) parts.Add(search);
        return string.Join(" ", parts);
    }

    /// <summary>
    /// 查看 Issue 详情 — 调 REST API 获取 Issue 信息，verbose=true 返回完整 JSON（从缓存读），默认精简输出
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueView, "查看 Issue 详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhIssueViewAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选,默认当前目录)", Required = false)] string? working_dir = null,
        [McpToolParameter("verbose=true 返回完整 JSON(从缓存读,不调 API); 默认 false 精简输出(调 API 更新缓存)", Required = false)] bool? verbose = null,
        [McpToolParameter("comments=true 附带评论列表", Required = false)] bool? comments = null,
        [McpToolParameter("web=true 只返回 Issue 浏览器 URL", Required = false)] bool? web = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            var apiPath = $"repos/{owner}/{repoName}/issues/{number}";
            if (web == true) {
                var issueResult = await client.SendAsync(HttpMethod.Get, apiPath, ct: cancellationToken).ConfigureAwait(false);
                if (!issueResult.Success) return Fail(issueResult.Error);
                var url = ExtractHtmlUrl(issueResult.Body);
                return string.IsNullOrEmpty(url) ? Fail("无法从 Issue 响应中解析 html_url") : Ok(url);
            }
            if (comments == true) {
                var issueResult = await client.SendAsync(HttpMethod.Get, apiPath, ct: cancellationToken).ConfigureAwait(false);
                if (!issueResult.Success) return Fail(issueResult.Error);
                var summary = verbose == true ? issueResult.Body : SummarizeIssue(issueResult.Body);
                var commentsResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/issues/{number}/comments", ct: cancellationToken).ConfigureAwait(false);
                if (!commentsResult.Success) return Fail(commentsResult.Error);
                return Ok($"{summary}\n\n## 评论\n{SummarizeComments(commentsResult.Body)}");
            }
            var cacheKey = BuildGhCacheKey("gh_issue_view", $"{owner}/{repoName}/{number}");
            return await GetOrFetchWithCacheAsync(client, cacheKey, apiPath, verbose, SummarizeIssue, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

    /// <summary>
    /// 创建 Issue — 支持标签/指派人/里程碑，调 REST API POST
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueCreate, "创建 Issue(支持标签/指派人/里程碑)", "github")]
    public async Task<ToolResult> GhIssueCreateAsync(
        [McpToolParameter("Issue 标题", Required = true)] string title,
        [McpToolParameter("Issue 内容(body)", Required = false)] string? body = null,
        [McpToolParameter("标签(可选,多个用逗号)", Required = false)] string? label = null,
        [McpToolParameter("指派人(可选)", Required = false)] string? assignee = null,
        [McpToolParameter("里程碑 ID(可选)", Required = false)] int? milestone = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var jsonBody = new GitHubJsonObjectBuilder()
                .String("title", title)
                .StringIf("body", body)
                .StringArrayFromCsvIf("labels", label)
                .StringArrayFromCsvIf("assignees", assignee)
                .NumberIf("milestone", milestone)
                .Build();
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, "Issue 创建成功") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 关闭 Issue — 可选附评论/关闭原因/重复标记，调 REST API PATCH state=closed
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueClose, "关闭 Issue(可附评论/原因/重复标记)", "github")]
    public async Task<ToolResult> GhIssueCloseAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("关闭评论(可选)", Required = false)] string? comment = null,
        [McpToolParameter("关闭原因(completed/not_planned,可选)", Required = false)] string? reason = null,
        [McpToolParameter("重复的 Issue 编号(可选,设置后 reason 自动 not_planned)", Required = false)] int? duplicate_of = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            var commentText = comment;
            if (duplicate_of is not null) commentText = string.IsNullOrWhiteSpace(commentText) ? $"Duplicate of #{duplicate_of}" : $"{commentText}\n\nDuplicate of #{duplicate_of}";
            if (!string.IsNullOrWhiteSpace(commentText)) {
                var commentBody = $$"""{"body":{{JsonEscapeString(commentText)}}}""";
                await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", commentBody, ct: cancellationToken).ConfigureAwait(false);
            }
            var stateReason = duplicate_of is not null ? "not_planned" : reason;
            var body = string.IsNullOrWhiteSpace(stateReason) ? """{"state":"closed"}""" : $$"""{"state":"closed","state_reason":"{{stateReason}}"}""";
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/issues/{number}", body, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已关闭 Issue {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 评论 Issue — 调 REST API POST comments 端点
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueComment, "评论 Issue", "github")]
    public async Task<ToolResult> GhIssueCommentAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("评论内容", Required = true)] string body,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            var reqBody = $$"""{"body":{{JsonEscapeString(body)}}}""";
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", reqBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已评论 Issue {number}") : Fail(result.Error);
        }).ConfigureAwait(false);
}