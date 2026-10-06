namespace McpToolDispatch;

/// <summary>
/// GitHub Repo 工具 — 直调 GitHub REST API（ADR 0073），替代原 gh repo 子命令包装
/// <para>clone 用本地 git 命令（非 API），create/fork/list/view 走 REST API</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 查看仓库详情 — 调 REST API 获取仓库信息，verbose=true 返回完整 JSON（从缓存读），默认精简输出，web=true 返回 URL
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoView, "查看仓库详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoViewAsync(
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        [McpToolParameter("verbose=true 返回完整 JSON(从缓存读,不调 API); 默认 false 精简输出(调 API 更新缓存)", Required = false)] bool? verbose = null,
        [McpToolParameter("web=true 只返回仓库浏览器 URL", Required = false)] bool? web = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (web == true) {
                var repoResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}", ct: cancellationToken).ConfigureAwait(false);
                if (!repoResult.Success) return Fail(repoResult.Error);
                var url = ExtractHtmlUrl(repoResult.Body);
                return string.IsNullOrEmpty(url) ? Fail("无法从仓库响应中解析 html_url") : Ok(url);
            }
            var cacheKey = BuildGhCacheKey("gh_repo_view", $"{owner}/{repoName}");
            return await GetOrFetchWithCacheAsync(client, cacheKey, $"repos/{owner}/{repoName}", verbose, SummarizeRepo, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

    /// <summary>
    /// 克隆仓库 — 支持浅克隆（--depth=1），走本地 git 命令（非 API）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoClone, "克隆仓库(支持浅克隆 --depth=1)", "github")]
    public async Task<ToolResult> GhRepoCloneAsync(
        [McpToolParameter("仓库名(owner/repo 或 URL)", Required = true)] string repo,
        [McpToolParameter("克隆目标目录(可选)", Required = false)] string? dir = null,
        [McpToolParameter("是否浅克隆(--depth=1,默认 false)", Required = false)] bool? shallow = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
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

    /// <summary>
    /// 创建仓库 — 支持 public/private/internal 可见性、描述、README 初始化、homepage、gitignore 模板、license 模板
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoCreate, "创建仓库(public/private/internal,支持 homepage/gitignore/license 模板)", "github")]
    public async Task<ToolResult> GhRepoCreateAsync(
        [McpToolParameter("仓库名", Required = true)] string name,
        [McpToolParameter("可见性(public/private/internal,默认 private)", Required = false)] string? visibility = null,
        [McpToolParameter("描述(可选)", Required = false)] string? description = null,
        [McpToolParameter("是否添加 README(可选)", Required = false)] bool? add_readme = null,
        [McpToolParameter("主页 URL(可选)", Required = false)] string? homepage = null,
        [McpToolParameter("gitignore 模板(可选,如 VisualStudio)", Required = false)] string? gitignore = null,
        [McpToolParameter("license 模板(可选,如 mit)", Required = false)] string? license = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var vis = string.IsNullOrWhiteSpace(visibility) ? "private" : visibility;
        var isPrivate = vis.Equals("private", StringComparison.OrdinalIgnoreCase);
        var isInternal = vis.Equals("internal", StringComparison.OrdinalIgnoreCase);

        var builder = new GitHubJsonObjectBuilder()
            .String("name", name)
            .Bool("private", isPrivate || isInternal);
        if (isInternal) builder.String("visibility", "internal");
        builder.StringIf("description", description)
            .BoolIfTrue("auto_init", add_readme)
            .StringIf("homepage", homepage)
            .StringIf("gitignore_template", gitignore)
            .StringIf("license_template", license);
        var jsonBody = builder.Build();

        var result = await _apiClient.SendAsync(HttpMethod.Post, "user/repos", jsonBody, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? OkBrief(result.Body, $"已创建仓库 {name}") : Fail(result.Error);
    }

    /// <summary>
    /// Fork 仓库 — 调 REST API 创建 Fork，可选克隆到本地、指定组织
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoFork, "Fork 仓库(可选指定组织)", "github")]
    public async Task<ToolResult> GhRepoForkAsync(
        [McpToolParameter("仓库名(owner/repo)", Required = true)] string repo,
        [McpToolParameter("是否克隆到本地(默认 false)", Required = false)] bool? clone = null,
        [McpToolParameter("Fork 到指定组织(可选)", Required = false)] string? org = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var parsed = ParseGitHubRepoRef(repo);
        if (parsed is null) return Fail("仓库名格式错误，应为 owner/repo");
        var (owner, repoName) = parsed.Value;

        var forkBody = string.IsNullOrWhiteSpace(org) ? "{}" : $$"""{"organization":"{{org}}"}""";
        var result = await _apiClient.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/forks", forkBody, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);

        if (clone == true) {
            if (_git is null) return OkBrief(result.Body, $"已 Fork {repo}（但未克隆：git 未配置）");
            var cloneResult = await _git.ExecuteAsync($"clone https://github.com/{repo}.git", working_dir, cancellationToken).ConfigureAwait(false);
            if (!cloneResult.Success) return OkBrief(result.Body, $"已 Fork {repo}（但克隆失败: {cloneResult.Error}）");
        }

        return OkBrief(result.Body, $"已 Fork {repo}");
    }

    /// <summary>
    /// 列出自己可访问的仓库 — 支持语言/可见性/source/fork 过滤，调 REST API 获取仓库列表，精简输出
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoList, "列出自己可访问的仓库(支持语言/可见性/source/fork 过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoListAsync(
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("语言过滤(可选)", Required = false)] string? language = null,
        [McpToolParameter("可见性过滤(public/private/internal,可选)", Required = false)] string? visibility = null,
        [McpToolParameter("只显示非 fork 仓库(可选)", Required = false)] bool? source = null,
        [McpToolParameter("只显示 fork 仓库(可选)", Required = false)] bool? fork = null,
        [McpToolParameter("仓库名(可选,被忽略,gh repo list 列自己的仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();

        var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
        if (!string.IsNullOrWhiteSpace(language)) query["language"] = language;
        if (!string.IsNullOrWhiteSpace(visibility)) query["visibility"] = visibility;
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user/repos", query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeRepoList(result.Body, source, fork));
    }

    /// <summary>
    /// 精简仓库列表 JSON — 只保留关键字段，去掉冗余 URL，便于人类浏览和 AI 解析；支持 source/fork 客户端过滤
    /// </summary>
    private static string SummarizeRepoList(string json, bool? source = null, bool? fork = null) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) {
                writer.WriteStartArray();
                foreach (var repo in doc.RootElement.EnumerateArray()) {
                    var isFork = repo.TryGetProperty("fork", out var f) && f.GetBoolean();
                    if (source == true && isFork) continue;
                    if (fork == true && !isFork) continue;
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
        } catch (Exception) {
            return json;
        }
    }

    /// <summary>
    /// 编辑仓库 — 修改描述/主页/可见性/默认分支/has_issues/has_wiki，调 REST API PATCH
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoEdit, "编辑仓库(description/homepage/visibility/default_branch/issues/wiki)", "github")]
    public async Task<ToolResult> GhRepoEditAsync(
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("描述(可选)", Required = false)] string? description = null,
        [McpToolParameter("主页 URL(可选)", Required = false)] string? homepage = null,
        [McpToolParameter("可见性(public/private/internal,可选)", Required = false)] string? visibility = null,
        [McpToolParameter("默认分支(可选)", Required = false)] string? default_branch = null,
        [McpToolParameter("是否启用 Issues(可选)", Required = false)] bool? has_issues = null,
        [McpToolParameter("是否启用 Wiki(可选)", Required = false)] bool? has_wiki = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var builder = new GitHubJsonObjectBuilder()
                .StringIf("description", description)
                .StringIf("homepage", homepage)
                .StringIf("visibility", visibility)
                .StringIf("default_branch", default_branch);
            if (has_issues is not null) builder.Bool("has_issues", has_issues.Value);
            if (has_wiki is not null) builder.Bool("has_wiki", has_wiki.Value);
            var jsonBody = builder.Build();
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已编辑仓库 {owner}/{repoName}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 删除仓库 — 调 REST API DELETE，需 yes=true 确认
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoDelete, "删除仓库(需 yes 确认)", "github")]
    public async Task<ToolResult> GhRepoDeleteAsync(
        [McpToolParameter("仓库名(owner/repo)", Required = true)] string repo,
        [McpToolParameter("是否跳过确认(默认 false)", Required = false)] bool? yes = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (yes != true) return Fail("删除仓库需要 yes=true 确认（此操作不可逆）");
            var result = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已删除仓库 {owner}/{repoName}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 归档仓库 — 调 REST API PATCH archived=true
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoArchive, "归档仓库", "github")]
    public async Task<ToolResult> GhRepoArchiveAsync(
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}", """{"archived":true}""", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已归档仓库 {owner}/{repoName}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 取消归档仓库 — 调 REST API PATCH archived=false
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoUnarchive, "取消归档仓库", "github")]
    public async Task<ToolResult> GhRepoUnarchiveAsync(
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}", """{"archived":false}""", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已取消归档仓库 {owner}/{repoName}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 重命名仓库 — 调 REST API POST /repos/{owner}/{repo}/rename
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoRename, "重命名仓库", "github")]
    public async Task<ToolResult> GhRepoRenameAsync(
        [McpToolParameter("新仓库名", Required = true)] string new_name,
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var jsonBody = new GitHubJsonObjectBuilder().String("new_name", new_name).Build();
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/rename", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已重命名仓库 {owner}/{repoName} → {owner}/{new_name}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 同步 Fork 仓库 — 调 REST API POST /repos/{owner}/{repo}/merge-upstream
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoSync, "同步 Fork 仓库(从上游拉取更新)", "github")]
    public async Task<ToolResult> GhRepoSyncAsync(
        [McpToolParameter("要同步的分支(可选,默认默认分支)", Required = false)] string? branch = null,
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var defaultBranch = branch ?? "main";
            var jsonBody = new GitHubJsonObjectBuilder().String("branch", defaultBranch).Build();
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/merge-upstream", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已同步仓库 {owner}/{repoName} 分支 {defaultBranch}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 设置默认分支 — 调 REST API PATCH default_branch
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoSetDefault, "设置默认分支", "github")]
    public async Task<ToolResult> GhRepoSetDefaultAsync(
        [McpToolParameter("默认分支名", Required = true)] string branch,
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var jsonBody = new GitHubJsonObjectBuilder().String("default_branch", branch).Build();
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已设置 {owner}/{repoName} 默认分支为 {branch}") : Fail(result.Error);
        }).ConfigureAwait(false);

    // === Autolink 管理 ===

    /// <summary>
    /// 列出仓库 Autolink 引用 — 调 REST API GET /keys/autolinks
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoAutolinkList, "列出仓库 Autolink 引用", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoAutolinkListAsync(
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/keys/autolinks", ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(SummarizeAutolinkList(result.Body));
        }).ConfigureAwait(false);

    /// <summary>
    /// 查看 Autolink 详情 — 调 REST API GET /keys/autolinks/{id}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoAutolinkView, "查看 Autolink 详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoAutolinkViewAsync(
        [McpToolParameter("Autolink ID", Required = true)] int autolink_id,
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/keys/autolinks/{autolink_id}", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok(result.Body) : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 创建 Autolink 引用 — 调 REST API POST /keys/autolinks
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoAutolinkCreate, "创建 Autolink 引用", "github")]
    public async Task<ToolResult> GhRepoAutolinkCreateAsync(
        [McpToolParameter("键前缀(如 TICKET-)", Required = true)] string key_prefix,
        [McpToolParameter("URL 模板(含 <num> 占位符,如 https://example.com/TICKET-<num>)", Required = true)] string url_template,
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var jsonBody = JsonSerializer.Serialize(new AutolinkCreateRequest { KeyPrefix = key_prefix, UrlTemplate = url_template }, GitHubApiJsonContext.Default.AutolinkCreateRequest);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/keys/autolinks", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, "Autolink 创建成功") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 删除 Autolink 引用 — 调 REST API DELETE /keys/autolinks/{id}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoAutolinkDelete, "删除 Autolink 引用", "github")]
    public async Task<ToolResult> GhRepoAutolinkDeleteAsync(
        [McpToolParameter("Autolink ID", Required = true)] int autolink_id,
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/keys/autolinks/{autolink_id}", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已删除 Autolink {autolink_id}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简 Autolink 列表 — 表格格式(id, key_prefix, url_template)
    /// </summary>
    private static string SummarizeAutolinkList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine("ID\t键前缀\tURL 模板");
            foreach (var al in doc.RootElement.EnumerateArray()) {
                var id = al.TryGetProperty("id", out var i) ? i.GetInt32() : 0;
                var prefix = al.TryGetProperty("key_prefix", out var kp) ? kp.GetString() ?? "" : "";
                var template = al.TryGetProperty("url_template", out var ut) ? ut.GetString() ?? "" : "";
                sb.AppendLine($"{id}\t{prefix}\t{template}");
            }
            return sb.ToString();
        } catch { return json; }
    }

    // === Deploy Key 管理 ===

    /// <summary>
    /// 列出仓库 Deploy Key — 调 REST API GET /keys
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoDeployKeyList, "列出仓库 Deploy Key", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoDeployKeyListAsync(
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/keys", ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(SummarizeDeployKeyList(result.Body));
        }).ConfigureAwait(false);

    /// <summary>
    /// 添加 Deploy Key — 调 REST API POST /keys
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoDeployKeyAdd, "添加 Deploy Key", "github")]
    public async Task<ToolResult> GhRepoDeployKeyAddAsync(
        [McpToolParameter("Key 标题", Required = true)] string title,
        [McpToolParameter("SSH public key 内容", Required = true)] string key,
        [McpToolParameter("只读(可选,默认 false)", Required = false)] bool? read_only = null,
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var jsonBody = JsonSerializer.Serialize(new DeployKeyAddRequest { Title = title, Key = key, ReadOnly = read_only }, GitHubApiJsonContext.Default.DeployKeyAddRequest);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/keys", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, "Deploy Key 添加成功") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 删除 Deploy Key — 调 REST API DELETE /keys/{id}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoDeployKeyDelete, "删除 Deploy Key", "github")]
    public async Task<ToolResult> GhRepoDeployKeyDeleteAsync(
        [McpToolParameter("Key ID", Required = true)] int key_id,
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/keys/{key_id}", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已删除 Deploy Key {key_id}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简 Deploy Key 列表 — 表格格式(id, title, read_only)
    /// </summary>
    private static string SummarizeDeployKeyList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine("ID\t标题\t只读\t创建时间");
            foreach (var k in doc.RootElement.EnumerateArray()) {
                var id = k.TryGetProperty("id", out var i) ? i.GetInt32() : 0;
                var title = k.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var ro = k.TryGetProperty("read_only", out var r) && r.GetBoolean();
                var created = k.TryGetProperty("created_at", out var c) ? c.GetString() ?? "" : "";
                sb.AppendLine($"{id}\t{title}\t{ro}\t{created}");
            }
            return sb.ToString();
        } catch { return json; }
    }

    // === Gitignore 模板 ===

    /// <summary>
    /// 列出可用 gitignore 模板 — 调 REST API GET /gitignore/templates
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoGitignoreList, "列出可用 gitignore 模板", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoGitignoreListAsync(
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, "gitignore/templates", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeGitignoreList(result.Body));
    }

    /// <summary>
    /// 精简 gitignore 模板列表 — 提取 names 数组
    /// </summary>
    private static string SummarizeGitignoreList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("names", out var names) && names.ValueKind == JsonValueKind.Array) {
                var sb = new StringBuilder(256);
                foreach (var n in names.EnumerateArray()) sb.AppendLine(n.GetString());
                return sb.ToString();
            }
            return json;
        } catch { return json; }
    }

    /// <summary>
    /// 查看 gitignore 模板内容 — 调 REST API GET /gitignore/templates/{name}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoGitignoreView, "查看 gitignore 模板内容", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoGitignoreViewAsync(
        [McpToolParameter("模板名(如 Java,Python,Node)", Required = true)] string name,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, $"gitignore/templates/{name}", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeGitignoreTemplate(result.Body));
    }

    /// <summary>
    /// 精简 gitignore 模板内容 — 提取 source 字段
    /// </summary>
    private static string SummarizeGitignoreTemplate(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("source", out var src)) return src.GetString() ?? "";
            return json;
        } catch { return json; }
    }

    // === License 模板 ===

    /// <summary>
    /// 列出常用 license — 调 REST API GET /licenses
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoLicenseList, "列出常用 license", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoLicenseListAsync(
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, "licenses", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeLicenseList(result.Body));
    }

    /// <summary>
    /// 精简 license 列表 — 表格格式(key, name, spdx_id)
    /// </summary>
    private static string SummarizeLicenseList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine("KEY\t名称\tSPDX ID");
            foreach (var lic in doc.RootElement.EnumerateArray()) {
                var key = lic.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                var name = lic.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var spdx = lic.TryGetProperty("spdx_id", out var s) ? s.GetString() ?? "" : "";
                sb.AppendLine($"{key}\t{name}\t{spdx}");
            }
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>
    /// 查看 license 详情 — 调 REST API GET /licenses/{key}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoLicenseView, "查看 license 详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoLicenseViewAsync(
        [McpToolParameter("license key(如 mit,apache-2.0,gpl-3.0)", Required = true)] string key,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, $"licenses/{key}", ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body) : Fail(result.Error);
    }
}