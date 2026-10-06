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
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("排序(stars/forks/updated,可选)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 desc)", Required = false)] string? order = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var q = string.IsNullOrWhiteSpace(query) ? "stars:>1" : query;
        var queryDict = new Dictionary<string, string> { ["q"] = q, ["per_page"] = (limit ?? 30).ToString() };
        if (!string.IsNullOrWhiteSpace(sort)) queryDict["sort"] = sort;
        queryDict["order"] = string.IsNullOrWhiteSpace(order) ? "desc" : order;
        var result = await _apiClient.SendAsync(HttpMethod.Get, "search/repositories", query: queryDict, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeSearchRepos(result.Body));
    }

    /// <summary>
    /// 精简搜索仓库结果 — 表格格式(full_name, stars, description)
    /// </summary>
    private static string SummarizeSearchRepos(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder(512);
            var totalCount = doc.RootElement.TryGetProperty("total_count", out var tc) ? tc.GetInt32() : 0;
            sb.AppendLine($"共 {totalCount} 个仓库");
            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array) {
                sb.AppendLine("仓库\tStars\t描述");
                foreach (var repo in items.EnumerateArray()) {
                    var fullName = repo.TryGetProperty("full_name", out var fn) ? fn.GetString() ?? "" : "";
                    var stars = repo.TryGetProperty("stargazers_count", out var s) ? s.GetInt32() : 0;
                    var desc = repo.TryGetProperty("description", out var d) ? (d.ValueKind == JsonValueKind.Null ? "" : d.GetString() ?? "") : "";
                    sb.AppendLine($"{fullName}\t{stars}\t{desc}");
                }
            }
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>
    /// 搜索 Issue — 调 GET /search/issues，自动加 is:issue 限定
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSearchIssues, "搜索 GitHub Issue", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhSearchIssuesAsync(
        [McpToolParameter("搜索查询(GitHub search 语法)", Required = false)] string? query = null,
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("排序(created/updated/comments,可选)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 desc)", Required = false)] string? order = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var q = string.IsNullOrWhiteSpace(query) ? "is:issue" : $"is:issue {query}";
        var queryDict = new Dictionary<string, string> { ["q"] = q, ["per_page"] = (limit ?? 30).ToString() };
        if (!string.IsNullOrWhiteSpace(sort)) queryDict["sort"] = sort;
        queryDict["order"] = string.IsNullOrWhiteSpace(order) ? "desc" : order;
        var result = await _apiClient.SendAsync(HttpMethod.Get, "search/issues", query: queryDict, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeSearchIssues(result.Body));
    }

    /// <summary>
    /// 搜索 PR — 调 GET /search/issues，自动加 is:pr 限定
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSearchPrs, "搜索 GitHub PR", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhSearchPrsAsync(
        [McpToolParameter("搜索查询(GitHub search 语法)", Required = false)] string? query = null,
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("排序(created/updated/comments,可选)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 desc)", Required = false)] string? order = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var q = string.IsNullOrWhiteSpace(query) ? "is:pr" : $"is:pr {query}";
        var queryDict = new Dictionary<string, string> { ["q"] = q, ["per_page"] = (limit ?? 30).ToString() };
        if (!string.IsNullOrWhiteSpace(sort)) queryDict["sort"] = sort;
        queryDict["order"] = string.IsNullOrWhiteSpace(order) ? "desc" : order;
        var result = await _apiClient.SendAsync(HttpMethod.Get, "search/issues", query: queryDict, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeSearchIssues(result.Body));
    }

    /// <summary>
    /// 精简搜索 Issue/PR 结果 — 表格格式(number, state, title, repository)
    /// </summary>
    private static string SummarizeSearchIssues(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder(512);
            var totalCount = doc.RootElement.TryGetProperty("total_count", out var tc) ? tc.GetInt32() : 0;
            sb.AppendLine($"共 {totalCount} 条结果");
            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array) {
                sb.AppendLine("编号\t状态\t标题\t仓库");
                foreach (var item in items.EnumerateArray()) {
                    var number = item.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
                    var state = item.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
                    var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var repoUrl = item.TryGetProperty("repository_url", out var ru) ? ru.GetString() ?? "" : "";
                    var repoName = repoUrl.Contains('/') ? repoUrl[(repoUrl.LastIndexOf('/') + 1)..] : "";
                    sb.AppendLine($"{number}\t{state}\t{title}\t{repoName}");
                }
            }
            return sb.ToString();
        } catch { return json; }
    }
}
