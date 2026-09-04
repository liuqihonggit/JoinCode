namespace McpToolDispatch;

/// <summary>
/// GitHub Repo 工具 — gh repo 子命令全套
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhRepoView, "查看仓库详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoViewAsync(
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder("repo view");
        if (!string.IsNullOrWhiteSpace(repo)) sb.Append($" {repo}");
        sb.Append(" --json name,owner,description,url,defaultBranchRef,visibility,createdAt,updatedAt,stargazerCount,forkCount");
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output) : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhRepoClone, "克隆仓库(支持浅克隆 --depth=1)", "github")]
    public async Task<ToolResult> GhRepoCloneAsync(
        [McpToolParameter("仓库名(owner/repo 或 URL)", Required = true)] string repo,
        [McpToolParameter("克隆目标目录(可选)", Required = false)] string? dir = null,
        [McpToolParameter("是否浅克隆(--depth=1,默认 false)", Required = false)] bool? shallow = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"repo clone {repo}");
        if (!string.IsNullOrWhiteSpace(dir)) sb.Append($" {dir}");
        if (shallow == true) sb.Append(" -- --depth=1");
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已克隆 {repo}") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhRepoCreate, "创建仓库(public/private/internal)", "github")]
    public async Task<ToolResult> GhRepoCreateAsync(
        [McpToolParameter("仓库名", Required = true)] string name,
        [McpToolParameter("可见性(public/private/internal,默认 private)", Required = false)] string? visibility = null,
        [McpToolParameter("描述(可选)", Required = false)] string? description = null,
        [McpToolParameter("是否添加 README(可选)", Required = false)] bool? add_readme = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var vis = visibility ?? "private";
        var sb = new StringBuilder($"repo create {name} --{vis}");
        if (!string.IsNullOrWhiteSpace(description)) sb.Append($" --description {Quote(description)}");
        if (add_readme == true) sb.Append(" --add-readme");
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已创建仓库 {name}") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhRepoFork, "Fork 仓库", "github")]
    public async Task<ToolResult> GhRepoForkAsync(
        [McpToolParameter("仓库名(owner/repo)", Required = true)] string repo,
        [McpToolParameter("是否克隆到本地(默认 false)", Required = false)] bool? clone = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"repo fork {repo}");
        if (clone == true) sb.Append(" --clone");
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已 Fork {repo}") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhRepoList, "列出自己可访问的仓库", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoListAsync(
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder("repo list");
        sb.Append($" --limit {limit ?? 30}");
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output) : Fail(result);
    }
}
