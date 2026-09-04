namespace McpToolDispatch;

/// <summary>
/// GitHub Actions Run 工具 — gh run 子命令全套
/// <para>避坑2/3/5: 大日志 maxLines 截断 + --job 精准拉 + 30s 超时</para>
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhRunList, "列出 Actions Run(支持状态/分支过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunListAsync(
        [McpToolParameter("数量限制(默认 20)", Required = false)] int? limit = null,
        [McpToolParameter("状态过滤(queued/in_progress/completed,可选)", Required = false)] string? status = null,
        [McpToolParameter("分支过滤(可选)", Required = false)] string? branch = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder("run list --json databaseId,status,conclusion,headBranch,event,workflowName,createdAt");
        sb.Append($" --limit {limit ?? 20}");
        if (!string.IsNullOrWhiteSpace(status)) sb.Append($" --status {status}");
        if (!string.IsNullOrWhiteSpace(branch)) sb.Append($" --branch {branch}");
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output) : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhRunView, "查看 Run 详情或日志(--job 精准拉单 job,maxLines 截断避免超时)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunViewAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("Job ID(可选,精准拉单 job 日志,避免全量日志超时)", Required = false)] string? job_id = null,
        [McpToolParameter("是否拉取日志(默认 false,仅看详情)", Required = false)] bool? log = null,
        [McpToolParameter("最大日志行数(默认 200,超出截断,避免撑爆上下文)", Required = false)] int? max_lines = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"run view {run_id}");
        if (!string.IsNullOrWhiteSpace(job_id)) sb.Append($" --job {job_id}");
        var wantLog = log == true;
        if (wantLog) sb.Append(" --log");

        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        if (!result.Success) return Fail(result);

        if (wantLog)
        {
            var maxLines = max_lines ?? 200;
            var truncated = TruncateLines(result.Output, maxLines);
            return Ok(truncated, $"Run {run_id} 日志:");
        }
        return Ok(result.Output);
    }

    [McpTool(GitHubToolNameConstants.GhRunRerun, "重跑 Actions Run(默认只重跑失败的 job)", "github")]
    public async Task<ToolResult> GhRunRerunAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("是否只重跑失败的 job(默认 true)", Required = false)] bool? failed_only = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"run rerun {run_id}");
        if (failed_only != false) sb.Append(" --failed");
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已重跑 Run {run_id}") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhRunCancel, "取消 Actions Run", "github")]
    public async Task<ToolResult> GhRunCancelAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync($"run cancel {run_id}", ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已取消 Run {run_id}") : Fail(result);
    }
}
