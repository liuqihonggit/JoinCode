namespace McpToolDispatch;

/// <summary>
/// GitHub Search 工具 — 直调 GitHub Search API 搜索 repos/issues/prs
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 搜索仓库 — 调 GET /search/repositories，支持 query/limit/order/sort
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSearchRepos, "搜索 GitHub 仓库", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhSearchReposAsync(
        [McpToolParameter("搜索查询(GitHub search 语法)", Required = false)] string? query = null,
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter("排序(stars/forks/updated,可选)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 desc)", Required = false)] string? order = null,
        [McpToolParameter(WellKnownParam.JsonFields)] string? json_fields = null,
        [McpToolParameter(WellKnownParam.Verbosity)] int? verbosity = null,
        [McpToolParameter(WellKnownParam.WorkingDir)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var q = string.IsNullOrWhiteSpace(query) ? "stars:>1" : query;
        var queryDict = new Dictionary<string, string> { ["q"] = q, ["per_page"] = (limit ?? 30).ToString() };
        if (!string.IsNullOrWhiteSpace(sort)) queryDict["sort"] = sort;
        queryDict["order"] = string.IsNullOrWhiteSpace(order) ? "desc" : order;
        var result = await _apiClient.SendAsync(HttpMethod.Get, "search/repositories", query: queryDict, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(FormatGhOutput(result.Body, verbosity, json_fields, SummarizeSearchRepos, "full_name,stargazers_count,description,html_url"));
    }

    /// <summary>
    /// 精简搜索仓库结果 — 表格格式(full_name, stars, description)
    /// </summary>
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

    /// <summary>
    /// 搜索 Issue — 调 GET /search/issues，自动加 is:issue 限定
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSearchIssues, "搜索 GitHub Issue", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhSearchIssuesAsync(
        [McpToolParameter("搜索查询(GitHub search 语法)", Required = false)] string? query = null,
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter("排序(created/updated/comments,可选)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 desc)", Required = false)] string? order = null,
        [McpToolParameter(WellKnownParam.JsonFields)] string? json_fields = null,
        [McpToolParameter(WellKnownParam.Verbosity)] int? verbosity = null,
        [McpToolParameter(WellKnownParam.WorkingDir)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var q = string.IsNullOrWhiteSpace(query) ? "is:issue" : $"is:issue {query}";
        var queryDict = new Dictionary<string, string> { ["q"] = q, ["per_page"] = (limit ?? 30).ToString() };
        if (!string.IsNullOrWhiteSpace(sort)) queryDict["sort"] = sort;
        queryDict["order"] = string.IsNullOrWhiteSpace(order) ? "desc" : order;
        var result = await _apiClient.SendAsync(HttpMethod.Get, "search/issues", query: queryDict, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(FormatGhOutput(result.Body, verbosity, json_fields, SummarizeSearchIssues, "number,title,state,html_url,repository_url"));
    }

    /// <summary>
    /// 搜索 PR — 调 GET /search/issues，自动加 is:pr 限定
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSearchPrs, "搜索 GitHub PR", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhSearchPrsAsync(
        [McpToolParameter("搜索查询(GitHub search 语法)", Required = false)] string? query = null,
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter("排序(created/updated/comments,可选)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 desc)", Required = false)] string? order = null,
        [McpToolParameter(WellKnownParam.JsonFields)] string? json_fields = null,
        [McpToolParameter(WellKnownParam.Verbosity)] int? verbosity = null,
        [McpToolParameter(WellKnownParam.WorkingDir)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var q = string.IsNullOrWhiteSpace(query) ? "is:pr" : $"is:pr {query}";
        var queryDict = new Dictionary<string, string> { ["q"] = q, ["per_page"] = (limit ?? 30).ToString() };
        if (!string.IsNullOrWhiteSpace(sort)) queryDict["sort"] = sort;
        queryDict["order"] = string.IsNullOrWhiteSpace(order) ? "desc" : order;
        var result = await _apiClient.SendAsync(HttpMethod.Get, "search/issues", query: queryDict, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(FormatGhOutput(result.Body, verbosity, json_fields, SummarizeSearchIssues, "number,title,state,html_url,repository_url"));
    }

    /// <summary>
    /// 精简搜索 Issue/PR 结果 — 表格格式(number, state, title, repository)
    /// </summary>
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
}
