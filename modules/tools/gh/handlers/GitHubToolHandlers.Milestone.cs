namespace McpToolDispatch;

/// <summary>
/// GitHub Milestone 工具 — 列出/查看 milestone
/// <para>ADR: 0073 — 直调 GitHub REST API</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>列出 Milestone — 调 GET /repos/{o}/{r}/milestones</summary>
    [McpTool(GitHubToolNameEnumConstants.GhMilestoneList, "列出 Milestone(支持状态过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhMilestoneListAsync(
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter("状态过滤(open/closed/all,默认 open)", Required = false)] string? state = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(common?.Repo, common?.WorkingDir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> {
                ["per_page"] = (limit ?? 30).ToString(),
                ["state"] = string.IsNullOrEmpty(state) ? "open" : state
            };
            return await GetOrFetchWithCacheAsync(client, $"repos/{owner}/{repoName}/milestones?{string.Join('&', query.Select(kv => $"{kv.Key}={kv.Value}"))}", common?.Verbosity, common?.JsonFields, SummarizeMilestoneList, "number,title,state,open_issues,closed_issues,html_url", cancellationToken, common?.Jq, common?.SkipLines, common?.StartLine, "gh_milestone_list").ConfigureAwait(false);
        }).ConfigureAwait(false);

    /// <summary>查看 Milestone 详情 — 调 GET /repos/{o}/{r}/milestones/{number}</summary>
    [McpTool(GitHubToolNameEnumConstants.GhMilestoneView, "查看 Milestone 详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhMilestoneViewAsync(
        [McpToolParameter("Milestone 编号", Required = true)] string number,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(common?.Repo, common?.WorkingDir, cancellationToken, async (client, owner, repoName) => {
            return await GetOrFetchWithCacheAsync(client, $"repos/{owner}/{repoName}/milestones/{number}", common?.Verbosity, common?.JsonFields, SummarizeMilestone, "number,title,state,description,open_issues,closed_issues,html_url,created_at,due_on", cancellationToken, common?.Jq, common?.SkipLines, common?.StartLine, "gh_milestone_view").ConfigureAwait(false);
        }).ConfigureAwait(false);

    /// <summary>精简 Milestone 列表 — 表格输出</summary>
    private static string SummarizeMilestoneList(string json) {
        try {
            var milestones = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.ListMilestoneItemResponse);
            if (milestones is null) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine("编号\t状态\t标题\tOpen\tClosed");
            foreach (var m in milestones) {
                sb.AppendLine($"{m.Number.Value}\t{m.State}\t{m.Title}\t{m.OpenIssues}\t{m.ClosedIssues}");
            }
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>精简 Milestone 详情 — 人类可读输出</summary>
    private static string SummarizeMilestone(string json) {
        try {
            var m = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.MilestoneItemResponse);
            if (m is null) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine($"Milestone #{m.Number.Value}: {m.Title}");
            sb.AppendLine($"状态: {m.State}");
            if (!string.IsNullOrEmpty(m.Description)) sb.AppendLine($"描述: {m.Description}");
            sb.AppendLine($"Issue: {m.OpenIssues} open, {m.ClosedIssues} closed");
            if (!string.IsNullOrEmpty(m.HtmlUrl)) sb.AppendLine($"URL: {m.HtmlUrl}");
            if (!string.IsNullOrEmpty(m.CreatedAt)) sb.AppendLine($"创建: {m.CreatedAt}");
            if (!string.IsNullOrEmpty(m.DueOn)) sb.AppendLine($"截止: {m.DueOn}");
            return sb.ToString();
        } catch { return json; }
    }
}
