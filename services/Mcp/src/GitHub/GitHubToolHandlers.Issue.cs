namespace McpToolDispatch;

/// <summary>
/// GitHub Issue 工具 — gh issue 子命令全套
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhIssueList, "列出 Issue(支持状态/标签/指派人过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhIssueListAsync(
        [McpToolParameter("状态(open/closed/all,默认 open)", Required = false)] string? state = null,
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("标签过滤(可选,多个用逗号)", Required = false)] string? label = null,
        [McpToolParameter("指派人过滤(可选)", Required = false)] string? assignee = null,
        [McpToolParameter("仓库(可选)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder("issue list --json number,title,state,url,author,labels,createdAt");
        sb.Append($" --state {state ?? "open"}");
        sb.Append($" --limit {limit ?? 30}");
        if (!string.IsNullOrWhiteSpace(label)) sb.Append($" --label {Quote(label)}");
        if (!string.IsNullOrWhiteSpace(assignee)) sb.Append($" --assignee {assignee}");
        sb.Append(RepoArg(repo));
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output) : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhIssueView, "查看 Issue 详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhIssueViewAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("仓库(可选)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"issue view {issue_number} --json number,title,state,body,author,createdAt,updatedAt,labels,assignees,comments");
        sb.Append(RepoArg(repo));
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output) : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhIssueCreate, "创建 Issue(支持标签/指派人)", "github")]
    public async Task<ToolResult> GhIssueCreateAsync(
        [McpToolParameter("Issue 标题", Required = true)] string title,
        [McpToolParameter("Issue 内容(body)", Required = false)] string? body = null,
        [McpToolParameter("标签(可选,多个用逗号)", Required = false)] string? label = null,
        [McpToolParameter("指派人(可选)", Required = false)] string? assignee = null,
        [McpToolParameter("仓库(可选)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder("issue create");
        sb.Append($" --title {Quote(title)}");
        if (!string.IsNullOrWhiteSpace(body)) sb.Append($" --body {Quote(body)}");
        if (!string.IsNullOrWhiteSpace(label)) sb.Append($" --label {Quote(label)}");
        if (!string.IsNullOrWhiteSpace(assignee)) sb.Append($" --assignee {assignee}");
        sb.Append(RepoArg(repo));
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, "Issue 创建成功") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhIssueClose, "关闭 Issue(可附评论)", "github")]
    public async Task<ToolResult> GhIssueCloseAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("关闭评论(可选)", Required = false)] string? comment = null,
        [McpToolParameter("仓库(可选)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"issue close {issue_number}");
        if (!string.IsNullOrWhiteSpace(comment)) sb.Append($" --comment {Quote(comment)}");
        sb.Append(RepoArg(repo));
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已关闭 Issue {issue_number}") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhIssueComment, "评论 Issue", "github")]
    public async Task<ToolResult> GhIssueCommentAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("评论内容", Required = true)] string body,
        [McpToolParameter("仓库(可选)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"issue comment {issue_number} --body {Quote(body)}");
        sb.Append(RepoArg(repo));
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已评论 Issue {issue_number}") : Fail(result);
    }
}
