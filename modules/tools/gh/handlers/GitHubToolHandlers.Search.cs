namespace McpToolDispatch;

/// <summary>
/// GitHub Search 工具 — 直调 GitHub Search API 搜索 repos/issues/prs/code/commits
/// <para>5 个搜索命令共享同一模板流程，差异仅在 SearchSpec（端点/查询前缀/摘要器/字段/工具名）。</para>
/// </summary>
public partial class GitHubToolHandlers {

    /// <summary>搜索规格 — 封装各搜索命令的差异点，驱动模板方法 SearchAsync</summary>
    private sealed record SearchSpec(
        string Endpoint,
        string? QueryFallback,
        string QueryPrefix,
        Func<string, string> Summarize,
        string CompactFields,
        string ToolName);

    /// <summary>搜索模板方法 — 5 步链式：构建查询 → 发送请求 → 格式化输出</summary>
    private async Task<ToolResult> SearchAsync(
        SearchSpec spec, string? query, int? limit, string? sort, string? order,
        GitHubCommonOptions? common, CancellationToken ct) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var q = BuildSearchQuery(spec, query);
        var queryDict = BuildSearchQueryDict(q, limit, sort, order);
        var result = await _apiClient.SendAsync(HttpMethod.Get, spec.Endpoint, query: queryDict, ct: ct).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(FormatGhOutput(result.Body, common?.Verbosity, common?.JsonFields,
            spec.Summarize, spec.CompactFields, common?.Jq, common?.SkipLines, common?.StartLine, spec.ToolName));
    }

    /// <summary>构建搜索查询 — 有前缀加前缀，无前缀用 fallback，都没有返回原始 query</summary>
    private static string BuildSearchQuery(SearchSpec spec, string? query)
        => string.IsNullOrWhiteSpace(query)
            ? (spec.QueryFallback ?? spec.QueryPrefix)
            : string.IsNullOrEmpty(spec.QueryPrefix)
                ? query
                : $"{spec.QueryPrefix} {query}";

    /// <summary>构建搜索查询字典 — q + per_page + sort + order</summary>
    private static Dictionary<string, string> BuildSearchQueryDict(string q, int? limit, string? sort, string? order) {
        var dict = new Dictionary<string, string> {
            ["q"] = q,
            ["per_page"] = (limit ?? 30).ToString(),
            ["order"] = string.IsNullOrWhiteSpace(order) ? "desc" : order,
        };
        if (!string.IsNullOrWhiteSpace(sort)) dict["sort"] = sort;
        return dict;
    }

    // === 5 个搜索命令 — 每个仅声明 [McpTool] + 委托 SearchAsync ===

    /// <summary>搜索仓库 — 调 GET /search/repositories</summary>
    [McpTool(GitHubToolNameEnumConstants.GhSearchRepos, "搜索 GitHub 仓库", "github", ConcurrencySafe = true)]
    public Task<ToolResult> GhSearchReposAsync(
        [McpToolParameter("搜索查询(GitHub search 语法)", Required = false)] string? query = null,
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter("排序(stars/forks/updated,可选)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 desc)", Required = false)] string? order = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => SearchAsync(s_searchReposSpec, query, limit, sort, order, common, cancellationToken);

    /// <summary>搜索 Issue — 调 GET /search/issues，自动加 is:issue 限定</summary>
    [McpTool(GitHubToolNameEnumConstants.GhSearchIssues, "搜索 GitHub Issue", "github", ConcurrencySafe = true)]
    public Task<ToolResult> GhSearchIssuesAsync(
        [McpToolParameter("搜索查询(GitHub search 语法)", Required = false)] string? query = null,
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter("排序(created/updated/comments,可选)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 desc)", Required = false)] string? order = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => SearchAsync(s_searchIssuesSpec, query, limit, sort, order, common, cancellationToken);

    /// <summary>搜索 PR — 调 GET /search/issues，自动加 is:pr 限定</summary>
    [McpTool(GitHubToolNameEnumConstants.GhSearchPrs, "搜索 GitHub PR", "github", ConcurrencySafe = true)]
    public Task<ToolResult> GhSearchPrsAsync(
        [McpToolParameter("搜索查询(GitHub search 语法)", Required = false)] string? query = null,
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter("排序(created/updated/comments,可选)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 desc)", Required = false)] string? order = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => SearchAsync(s_searchPrsSpec, query, limit, sort, order, common, cancellationToken);

    /// <summary>搜索代码 — 调 GET /search/code</summary>
    [McpTool(GitHubToolNameEnumConstants.GhSearchCode, "搜索 GitHub 代码", "github", ConcurrencySafe = true)]
    public Task<ToolResult> GhSearchCodeAsync(
        [McpToolParameter("搜索查询(GitHub code search 语法,如 auto_merge repo:owner/repo)", Required = true)] string query,
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter("排序(indexed,可选)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 desc)", Required = false)] string? order = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => SearchAsync(s_searchCodeSpec, query, limit, sort, order, common, cancellationToken);

    /// <summary>搜索提交 — 调 GET /search/commits</summary>
    [McpTool(GitHubToolNameEnumConstants.GhSearchCommits, "搜索 GitHub 提交", "github", ConcurrencySafe = true)]
    public Task<ToolResult> GhSearchCommitsAsync(
        [McpToolParameter("搜索查询(GitHub commit search 语法,如 fix repo:owner/repo)", Required = true)] string query,
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter("排序(author-date/committer-date,可选)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 desc)", Required = false)] string? order = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => SearchAsync(s_searchCommitsSpec, query, limit, sort, order, common, cancellationToken);

    // === SearchSpec 实例 — 每个搜索命令的差异点 ===

    private static readonly SearchSpec s_searchReposSpec = new(
        "search/repositories", "stars:>1", "", SummarizeSearchRepos,
        "full_name,stargazers_count,description,html_url", "gh_search_repos");

    private static readonly SearchSpec s_searchIssuesSpec = new(
        "search/issues", "is:issue", "is:issue", SummarizeSearchIssues,
        "number,title,state,html_url,repository_url", "gh_search_issues");

    private static readonly SearchSpec s_searchPrsSpec = new(
        "search/issues", "is:pr", "is:pr", SummarizeSearchIssues,
        "number,title,state,html_url,repository_url", "gh_search_prs");

    private static readonly SearchSpec s_searchCodeSpec = new(
        "search/code", null, "", SummarizeSearchCode,
        "name,path,html_url,repository", "gh_search_code");

    private static readonly SearchSpec s_searchCommitsSpec = new(
        "search/commits", null, "", SummarizeSearchCommits,
        "sha,commit.message,html_url,repository", "gh_search_commits");

    // === 摘要器 — 表格化搜索结果 ===

    /// <summary>精简搜索仓库结果 — 表格格式(full_name, stars, description)</summary>
    private static string SummarizeSearchRepos(string json) {
        try {
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.SearchRepoResponse);
            if (resp is null) return json;
            var sb = new StringBuilder(512);
            sb.AppendLine($"共 {resp.TotalCount} 个仓库");
            sb.AppendLine("仓库\tStars\t描述");
            foreach (var repo in resp.Items) sb.AppendLine($"{repo.FullName}\t{repo.StargazersCount}\t{repo.Description}");
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>精简搜索 Issue/PR 结果 — 表格格式(number, state, title, repository)</summary>
    private static string SummarizeSearchIssues(string json) {
        try {
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.SearchIssueResponse);
            if (resp is null) return json;
            var sb = new StringBuilder(512);
            sb.AppendLine($"共 {resp.TotalCount} 条结果");
            sb.AppendLine("编号\t状态\t标题\t仓库");
            foreach (var item in resp.Items) {
                var repoUrl = item.RepositoryUrl ?? "";
                var repoName = repoUrl.Contains('/') ? repoUrl[(repoUrl.LastIndexOf('/') + 1)..] : "";
                sb.AppendLine($"{item.Number}\t{item.State}\t{item.Title}\t{repoName}");
            }
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>精简搜索代码结果 — 表格格式(path, repository)</summary>
    private static string SummarizeSearchCode(string json) {
        try {
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.SearchCodeResponse);
            if (resp is null) return json;
            var sb = new StringBuilder(512);
            sb.AppendLine($"共 {resp.TotalCount} 个结果");
            sb.AppendLine("路径\t仓库");
            foreach (var item in resp.Items)
                sb.AppendLine($"{item.Path}\t{item.Repository?.FullName ?? ""}");
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>精简搜索提交结果 — 表格格式(sha, message, repository)</summary>
    private static string SummarizeSearchCommits(string json) {
        try {
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.SearchCommitResponse);
            if (resp is null) return json;
            var sb = new StringBuilder(512);
            sb.AppendLine($"共 {resp.TotalCount} 个提交");
            sb.AppendLine("SHA\t消息\t仓库");
            foreach (var item in resp.Items) {
                var sha = item.Sha.Length > 7 ? item.Sha[..7] : item.Sha;
                var msg = item.Commit?.Message ?? "";
                if (msg.Contains('\n')) msg = msg[..msg.IndexOf('\n')];
                sb.AppendLine($"{sha}\t{msg}\t{item.Repository?.FullName ?? ""}");
            }
            return sb.ToString();
        } catch { return json; }
    }
}
