namespace McpToolDispatch;

/// <summary>
/// GitHub Repo 工具 — 直调 GitHub REST API（ADR 0073），替代原 gh repo 子命令包装
/// <para>clone 用本地 git 命令（非 API），create/fork/list/view 走 REST API</para>
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhRepoView, "查看仓库详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoViewAsync(
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;

        var result = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}", ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body) : Fail(result.Error);
    }

    [McpTool(GitHubToolNameConstants.GhRepoClone, "克隆仓库(支持浅克隆 --depth=1)", "github")]
    public async Task<ToolResult> GhRepoCloneAsync(
        [McpToolParameter("仓库名(owner/repo 或 URL)", Required = true)] string repo,
        [McpToolParameter("克隆目标目录(可选)", Required = false)] string? dir = null,
        [McpToolParameter("是否浅克隆(--depth=1,默认 false)", Required = false)] bool? shallow = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_git is null) return Fail("git 命令执行器未配置（IGitCommandRunner 未注入）");

        var cloneUrl = repo.StartsWith("http", StringComparison.OrdinalIgnoreCase) || repo.Contains('@')
            ? repo
            : $"https://github.com/{repo}.git";

        var sb = new StringBuilder($"clone {cloneUrl}");
        if (!string.IsNullOrWhiteSpace(dir)) sb.Append($" {dir}");
        if (shallow == true) sb.Append(" --depth=1");

        var result = await _git.ExecuteAsync(sb.ToString(), working_dir, cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Output, $"已克隆 {repo}") : Fail(result.Error);
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
        if (_apiClient is null) return ApiClientNotConfigured();
        var vis = string.IsNullOrWhiteSpace(visibility) ? "private" : visibility;
        var isPrivate = vis.Equals("private", StringComparison.OrdinalIgnoreCase);
        var isInternal = vis.Equals("internal", StringComparison.OrdinalIgnoreCase);

        var bodySb = new StringBuilder();
        bodySb.Append('{');
        bodySb.Append("\"name\":" + JsonEscapeString(name));
        bodySb.Append(",\"private\":" + (isPrivate || isInternal ? "true" : "false"));
        if (isInternal) bodySb.Append(",\"visibility\":\"internal\"");
        if (!string.IsNullOrWhiteSpace(description)) bodySb.Append(",\"description\":" + JsonEscapeString(description));
        if (add_readme == true) bodySb.Append(",\"auto_init\":true");
        bodySb.Append('}');

        var result = await _apiClient.SendAsync(HttpMethod.Post, "user/repos", bodySb.ToString(), ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body, $"已创建仓库 {name}") : Fail(result.Error);
    }

    [McpTool(GitHubToolNameConstants.GhRepoFork, "Fork 仓库", "github")]
    public async Task<ToolResult> GhRepoForkAsync(
        [McpToolParameter("仓库名(owner/repo)", Required = true)] string repo,
        [McpToolParameter("是否克隆到本地(默认 false)", Required = false)] bool? clone = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var parsed = ParseGitHubRepoRef(repo);
        if (parsed is null) return Fail("仓库名格式错误，应为 owner/repo");
        var (owner, repoName) = parsed.Value;

        var result = await _apiClient.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/forks", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);

        if (clone == true)
        {
            if (_git is null) return Ok(result.Body, $"已 Fork {repo}（但未克隆：git 未配置）");
            var cloneResult = await _git.ExecuteAsync($"clone https://github.com/{repo}.git", working_dir, cancellationToken).ConfigureAwait(false);
            if (!cloneResult.Success) return Ok(result.Body, $"已 Fork {repo}（但克隆失败: {cloneResult.Error}）");
        }

        return Ok(result.Body, $"已 Fork {repo}");
    }

    [McpTool(GitHubToolNameConstants.GhRepoList, "列出自己可访问的仓库", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoListAsync(
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();

        var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user/repos", query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeRepoList(result.Body));
    }

    /// <summary>
    /// 精简仓库列表 JSON — 只保留关键字段，去掉冗余 URL，便于人类浏览和 AI 解析
    /// </summary>
    private static string SummarizeRepoList(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartArray();
                foreach (var repo in doc.RootElement.EnumerateArray())
                {
                    writer.WriteStartObject();
                    CopyProperty(repo, writer, "name");
                    CopyProperty(repo, writer, "full_name");
                    CopyProperty(repo, writer, "private");
                    CopyProperty(repo, writer, "fork");
                    CopyProperty(repo, writer, "description");
                    CopyProperty(repo, writer, "language");
                    CopyProperty(repo, writer, "stargazers_count");
                    CopyProperty(repo, writer, "updated_at");
                    CopyProperty(repo, writer, "default_branch");
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
        catch (Exception)
        {
            return json;
        }
    }
}
