namespace McpToolDispatch;

/// <summary>
/// GitHub PR 工具 — gh pr 子命令全套
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhPrView, "查看 PR 详情(号/标题/状态/URL/body/变更统计)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrViewAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("工作目录(可选,默认当前目录)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync($"pr view {pr_number} --json number,title,state,url,body,additions,deletions,changedFiles,author,createdAt,updatedAt", ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output) : Fail(result);
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
        var sb = new StringBuilder("pr list --json number,title,state,url,author,updatedAt");
        sb.Append($" --state {state ?? "open"}");
        sb.Append($" --limit {limit ?? 30}");
        if (!string.IsNullOrWhiteSpace(author)) sb.Append($" --author {author}");
        sb.Append(RepoArg(repo));
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output) : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhPrDiff, "查看 PR diff(patch 文本)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrDiffAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync($"pr diff {pr_number}", ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output) : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhPrChecks, "查看 PR 的 CI 检查状态(pass/fail/pending/skipping,skipping 非失败)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhPrChecksAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync($"pr checks {pr_number}", ResolveWorkDir(working_dir), cancellationToken);
        if (!result.Success) return Fail(result);

        // 解析 pr checks 输出: <check-name>\t<status>\t<duration>\t<url>
        // 避坑4: skipping 是依赖链跳过,非失败
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        var passCount = 0; var failCount = 0; var pendingCount = 0; var skipCount = 0;
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var status = parts[1].Trim();
            sb.AppendLine(line);
            switch (status.ToLowerInvariant())
            {
                case "pass": passCount++; break;
                case "fail": failCount++; break;
                case "pending": pendingCount++; break;
                case "skipping": skipCount++; break;
            }
        }
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
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var method = merge_method ?? "squash";
        var sb = new StringBuilder($"pr merge {pr_number} --{method}");
        if (auto_merge == true) sb.Append(" --auto");
        if (delete_branch == true) sb.Append(" --delete-branch");
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, "PR 合并成功") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhPrCheckout, "检出 PR 分支到本地", "github")]
    public async Task<ToolResult> GhPrCheckoutAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync($"pr checkout {pr_number}", ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已检出 PR {pr_number}") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhPrClose, "关闭 PR(可附评论)", "github")]
    public async Task<ToolResult> GhPrCloseAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("关闭评论(可选)", Required = false)] string? comment = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"pr close {pr_number}");
        if (!string.IsNullOrWhiteSpace(comment)) sb.Append($" --comment {Quote(comment)}");
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已关闭 PR {pr_number}") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhPrReopen, "重新打开 PR", "github")]
    public async Task<ToolResult> GhPrReopenAsync(
        [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync($"pr reopen {pr_number}", ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已重开 PR {pr_number}") : Fail(result);
    }
}
