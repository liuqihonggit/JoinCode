namespace McpToolDispatch;

/// <summary>
/// GitHub P4 命令工具 — browse/cache/ruleset/status/codespace/discussion/project/alias/extension/licenses
/// <para>低频命令组，直调 GitHub REST API 或 GraphQL（ADR 0073）</para>
/// </summary>
public partial class GitHubToolHandlers {

    /// <summary>
    /// 构建 GraphQL 请求 JSON — 用 DTO 序列化，自动转义双引号，消除内插原始字符串的 } 转义问题
    /// </summary>
    private static string BuildGraphQL(string query)
        => JsonSerializer.Serialize(new GraphQLRequest { Query = query }, GitHubApiJsonContext.Safe.GraphQLRequest);

    // === Browse ===

    /// <summary>
    /// 在浏览器中打开仓库/Issue/PR/commit 等 — 构造 GitHub URL，no_browser=true 只返回 URL
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhBrowse, "在浏览器中打开仓库/Issue/PR/commit(或只返回 URL)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhBrowseAsync(
        [McpToolParameter("Issue/PR 编号、文件路径或 commit SHA(可选)", Required = false)] string? target = null,
        [McpToolParameter("分支名(可选)", Required = false)] string? branch = null,
        [McpToolParameter("commit SHA(可选,默认最新)", Required = false)] string? commit = null,
        [McpToolParameter("打开 actions 页(可选)", Required = false)] bool? actions = null,
        [McpToolParameter("打开 releases 页(可选)", Required = false)] bool? releases = null,
        [McpToolParameter("打开 projects 页(可选)", Required = false)] bool? projects = null,
        [McpToolParameter("打开 settings 页(可选)", Required = false)] bool? settings = null,
        [McpToolParameter("打开 blame 视图(可选)", Required = false)] bool? blame = null,
        [McpToolParameter("只返回 URL 不打开浏览器(默认 false)", Required = false)] bool? no_browser = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var url = $"https://github.com/{owner}/{repoName}";
            if (actions == true) url += "/actions";
            else if (releases == true) url += "/releases";
            else if (projects == true) url += "/projects";
            else if (settings == true) url += "/settings";
            else if (!string.IsNullOrWhiteSpace(branch)) url += $"/tree/{branch}";
            else if (!string.IsNullOrWhiteSpace(commit)) url += $"/commit/{commit}";
            else if (!string.IsNullOrWhiteSpace(target)) {
                if (int.TryParse(target, out var num)) url += $"/issues/{num}";
                else if (target.Length == 40 && target.All(c => "0123456789abcdef".Contains(c))) url += $"/commit/{target}";
                else url += $"/tree/HEAD/{target}";
            }
            if (blame == true && !string.IsNullOrWhiteSpace(target)) url = $"https://github.com/{owner}/{repoName}/blame/HEAD/{target}";
            return Ok(url, no_browser == true ? "URL:" : "在浏览器中打开（设 no_browser=true 只返回 URL）:");
        }).ConfigureAwait(false);

    // === Cache（Actions 缓存）===

    /// <summary>
    /// 列出 Actions 缓存 — 调 GET /actions/caches，支持 branch/ref/key 过滤
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhCacheList, "列出 Actions 缓存(支持 branch/ref/key 过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhCacheListAsync(
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("分支过滤(可选)", Required = false)] string? branch = null,
        [McpToolParameter("ref 过滤(可选)", Required = false)] string? @ref = null,
        [McpToolParameter("key 过滤(可选,模糊匹配)", Required = false)] string? key = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
            if (!string.IsNullOrWhiteSpace(branch)) query["ref"] = $"refs/heads/{branch}";
            if (!string.IsNullOrWhiteSpace(@ref)) query["ref"] = @ref;
            if (!string.IsNullOrWhiteSpace(key)) query["key"] = key;
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/caches", query: query, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(SummarizeCacheList(result.Body));
        }).ConfigureAwait(false);

    /// <summary>精简缓存列表 — 表格格式(id, key, ref, size, last_used)</summary>
    private static string SummarizeCacheList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder(256);
            var totalCount = doc.RootElement.TryGetProperty("total_count", out var tc) ? tc.GetInt32() : 0;
            sb.AppendLine($"共 {totalCount} 个缓存");
            sb.AppendLine("ID\tKey\tRef\t大小(MB)\t最后使用");
            if (doc.RootElement.TryGetProperty("actions_caches", out var caches)) {
                foreach (var c in caches.EnumerateArray()) {
                    var id = c.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
                    var keyVal = c.TryGetProperty("key", out var kEl) ? kEl.GetString() ?? "" : "";
                    var refVal = c.TryGetProperty("ref", out var rEl) ? rEl.GetString() ?? "" : "";
                    var size = c.TryGetProperty("size_in_bytes", out var sEl) ? sEl.GetInt64() / 1024.0 / 1024.0 : 0;
                    var lastUsed = c.TryGetProperty("last_used_at", out var luEl) ? luEl.GetString() ?? "" : "";
                    sb.AppendLine($"{id}\t{keyVal}\t{refVal}\t{size:F1}\t{lastUsed}");
                }
            }
            return sb.ToString();
        } catch (Exception ex) { return $"解析缓存列表失败: {ex.Message}"; }
    }

    /// <summary>
    /// 删除 Actions 缓存 — 调 DELETE /actions/caches/{id}，all=true 删除全部
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhCacheDelete, "删除 Actions 缓存(指定 ID 或 all=true 删全部)", "github")]
    public async Task<ToolResult> GhCacheDeleteAsync(
        [McpToolParameter("缓存 ID(可选,all=true 时忽略)", Required = false)] long? cache_id = null,
        [McpToolParameter("删除全部缓存(默认 false)", Required = false)] bool? all = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (all == true) {
                var listResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/caches", ct: cancellationToken).ConfigureAwait(false);
                if (!listResult.Success) return Fail(listResult.Error);
                using var doc = JsonDocument.Parse(listResult.Body);
                if (!doc.RootElement.TryGetProperty("actions_caches", out var caches)) return Ok("无缓存可删除");
                var count = 0;
                foreach (var c in caches.EnumerateArray()) {
                    var id = c.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
                    if (id > 0) {
                        var delResult = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/actions/caches/{id}", ct: cancellationToken).ConfigureAwait(false);
                        if (delResult.Success) count++;
                    }
                }
                return Ok($"已删除 {count} 个缓存");
            }
            if (cache_id is null or <= 0) return Fail("需要 cache_id 或 all=true");
            var result = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/actions/caches/{cache_id}", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已删除缓存 {cache_id}") : Fail(result.Error);
        }).ConfigureAwait(false);

    // === Ruleset（仓库规则集）===

    /// <summary>
    /// 列出仓库规则集 — 调 GET /rulesets，支持 org 范围
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRulesetList, "列出仓库/组织规则集", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRulesetListAsync(
        [McpToolParameter("组织名(可选,列出组织级规则集)", Required = false)] string? org = null,
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
            var path = !string.IsNullOrWhiteSpace(org) ? $"orgs/{org}/rulesets" : $"repos/{owner}/{repoName}/rulesets";
            var result = await client.SendAsync(HttpMethod.Get, path, query: query, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(SummarizeRulesetList(result.Body));
        }).ConfigureAwait(false);

    /// <summary>精简规则集列表 — 表格格式(id, name, target, enforcement)</summary>
    private static string SummarizeRulesetList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine("ID\t名称\t目标\t执行级别");
            foreach (var r in doc.RootElement.EnumerateArray()) {
                var id = r.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
                var name = r.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                var target = r.TryGetProperty("target", out var tEl) ? tEl.GetString() ?? "" : "";
                var enforcement = r.TryGetProperty("enforcement", out var eEl) ? eEl.GetString() ?? "" : "";
                sb.AppendLine($"{id}\t{name}\t{target}\t{enforcement}");
            }
            return sb.ToString();
        } catch (Exception ex) { return $"解析规则集列表失败: {ex.Message}"; }
    }

    /// <summary>
    /// 查看规则集详情 — 调 GET /rulesets/{id}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRulesetView, "查看规则集详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRulesetViewAsync(
        [McpToolParameter("规则集 ID", Required = true)] long ruleset_id,
        [McpToolParameter("组织名(可选,查看组织级规则集)", Required = false)] string? org = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var path = !string.IsNullOrWhiteSpace(org) ? $"orgs/{org}/rulesets/{ruleset_id}" : $"repos/{owner}/{repoName}/rulesets/{ruleset_id}";
            var result = await client.SendAsync(HttpMethod.Get, path, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok(result.Body) : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 检查分支适用的规则 — 调 GET /rulesets-rs/{branch}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRulesetCheck, "检查分支适用的规则集", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRulesetCheckAsync(
        [McpToolParameter("分支名", Required = true)] string branch,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/rulesets-rs/{branch}", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok(result.Body) : Fail(result.Error);
        }).ConfigureAwait(false);

    // === Status（跨仓库状态）===

    /// <summary>
    /// 跨仓库状态 — 聚合 assigned issues/PRs、review requests、mentions
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhStatus, "跨仓库状态(指派的 issue/PR、审查请求、提及)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhStatusAsync(
        [McpToolParameter("组织名(可选,限定组织范围)", Required = false)] string? org = null,
        [McpToolParameter("排除仓库(可选,逗号分隔 owner/repo)", Required = false)] string? exclude = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var excludeSet = exclude?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet() ?? new HashSet<string>();
        var orgFilter = !string.IsNullOrWhiteSpace(org) ? $" org:{org}" : "";
        var sb = new StringBuilder(512);

        var assignedIssues = await _apiClient.SendAsync(HttpMethod.Get, "search/issues", query: new Dictionary<string, string> { ["q"] = $"is:issue is:open assignee:@me{orgFilter}", ["per_page"] = "20" }, ct: cancellationToken).ConfigureAwait(false);
        if (assignedIssues.Success) sb.AppendLine("=== 指派的 Issue ===").AppendLine(SummarizeSearchResults(assignedIssues.Body, excludeSet));

        var assignedPrs = await _apiClient.SendAsync(HttpMethod.Get, "search/issues", query: new Dictionary<string, string> { ["q"] = $"is:pr is:open assignee:@me{orgFilter}", ["per_page"] = "20" }, ct: cancellationToken).ConfigureAwait(false);
        if (assignedPrs.Success) sb.AppendLine("=== 指派的 PR ===").AppendLine(SummarizeSearchResults(assignedPrs.Body, excludeSet));

        var reviewRequests = await _apiClient.SendAsync(HttpMethod.Get, "search/issues", query: new Dictionary<string, string> { ["q"] = $"is:pr is:open review-requested:@me{orgFilter}", ["per_page"] = "20" }, ct: cancellationToken).ConfigureAwait(false);
        if (reviewRequests.Success) sb.AppendLine("=== 审查请求 ===").AppendLine(SummarizeSearchResults(reviewRequests.Body, excludeSet));

        var mentions = await _apiClient.SendAsync(HttpMethod.Get, "search/issues", query: new Dictionary<string, string> { ["q"] = $"is:open mentions:@me{orgFilter}", ["per_page"] = "20" }, ct: cancellationToken).ConfigureAwait(false);
        if (mentions.Success) sb.AppendLine("=== 提及 ===").AppendLine(SummarizeSearchResults(mentions.Body, excludeSet));

        return Ok(sb.ToString());
    }

    /// <summary>精简搜索结果 — 表格格式(repo, number, title)，支持 exclude 过滤</summary>
    private static string SummarizeSearchResults(string json, HashSet<string> excludeSet) {
        try {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder(256);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return "";
            foreach (var item in items.EnumerateArray()) {
                var repoUrl = item.TryGetProperty("repository_url", out var ruEl) ? ruEl.GetString() ?? "" : "";
                var repoName = repoUrl.EndsWith("/repos") ? "" : repoUrl[(repoUrl.LastIndexOf("/repos/") + 7)..];
                if (excludeSet.Contains(repoName)) continue;
                var number = item.TryGetProperty("number", out var nEl) ? nEl.GetInt32() : 0;
                var title = item.TryGetProperty("title", out var tEl) ? tEl.GetString() ?? "" : "";
                var state = item.TryGetProperty("state", out var sEl) ? sEl.GetString() ?? "" : "";
                sb.AppendLine($"{repoName}#{number}\t{state}\t{title}");
            }
            return sb.ToString();
        } catch { return ""; }
    }

    // === Codespace ===

    /// <summary>
    /// 列出 Codespace — 调 GET /user/codespaces
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhCodespaceList, "列出 Codespace", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhCodespaceListAsync(
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("仓库(可选,过滤指定仓库)", Required = false)] string? repo = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
        var path = "user/codespaces";
        if (!string.IsNullOrWhiteSpace(repo)) {
            var parsed = ParseGitHubRepoRef(repo);
            if (parsed is not null) path = $"repos/{parsed.Value.owner}/{parsed.Value.repo}/codespaces";
        }
        var result = await _apiClient.SendAsync(HttpMethod.Get, path, query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeCodespaceList(result.Body));
    }

    /// <summary>精简 Codespace 列表 — 表格格式(name, display_name, repo, state, branch)</summary>
    private static string SummarizeCodespaceList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder(256);
            if (doc.RootElement.TryGetProperty("codespaces", out var codespaces)) {
                sb.AppendLine("名称\t显示名\t仓库\t状态\t分支");
                foreach (var c in codespaces.EnumerateArray()) {
                    var name = c.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                    var displayName = c.TryGetProperty("display_name", out var dnEl) ? dnEl.GetString() ?? "" : "";
                    var repo = c.TryGetProperty("repository", out var rEl) && rEl.TryGetProperty("full_name", out var fnEl) ? fnEl.GetString() ?? "" : "";
                    var state = c.TryGetProperty("state", out var sEl) ? sEl.GetString() ?? "" : "";
                    var branch = c.TryGetProperty("git_status", out var gsEl) && gsEl.TryGetProperty("ref", out var refEl) ? refEl.GetString() ?? "" : "";
                    sb.AppendLine($"{name}\t{displayName}\t{repo}\t{state}\t{branch}");
                }
            }
            return sb.ToString();
        } catch (Exception ex) { return $"解析 Codespace 列表失败: {ex.Message}"; }
    }

    /// <summary>
    /// 创建 Codespace — 调 POST /user/codespaces，需指定仓库 + 分支
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhCodespaceCreate, "创建 Codespace(需指定仓库+分支)", "github")]
    public async Task<ToolResult> GhCodespaceCreateAsync(
        [McpToolParameter("仓库名(owner/repo)", Required = true)] string repo,
        [McpToolParameter("分支名(可选,默认仓库默认分支)", Required = false)] string? branch = null,
        [McpToolParameter("machine 类型(可选,如 basicLinux32gb)", Required = false)] string? machine = null,
        [McpToolParameter("devcontainer 路径(可选)", Required = false)] string? devcontainer_path = null,
        [McpToolParameter("显示名(可选)", Required = false)] string? display_name = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var parsed = ParseGitHubRepoRef(repo);
        if (parsed is null) return Fail("仓库名格式错误，应为 owner/repo");
        var (owner, repoName) = parsed.Value;
        var body = JsonSerializer.Serialize(new CodespaceCreateRequest {
            RepositoryId = 0,
            Ref = branch,
            Machine = machine,
            DevcontainerPath = devcontainer_path,
            DisplayName = display_name
        }, GitHubApiJsonContext.Safe.CodespaceCreateRequest);
        var repoResult = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}", ct: cancellationToken).ConfigureAwait(false);
        if (!repoResult.Success) return Fail(repoResult.Error);
        using (var doc = JsonDocument.Parse(repoResult.Body)) {
            var repoId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
            body = JsonSerializer.Serialize(new CodespaceCreateRequest {
                RepositoryId = repoId,
                Ref = branch,
                Machine = machine,
                DevcontainerPath = devcontainer_path,
                DisplayName = display_name
            }, GitHubApiJsonContext.Safe.CodespaceCreateRequest);
        }
        var result = await _apiClient.SendAsync(HttpMethod.Post, "user/codespaces", body, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? OkBrief(result.Body, $"已创建 Codespace for {repo}") : Fail(result.Error);
    }

    /// <summary>
    /// 删除 Codespace — 调 DELETE /user/codespaces/{name}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhCodespaceDelete, "删除 Codespace", "github")]
    public async Task<ToolResult> GhCodespaceDeleteAsync(
        [McpToolParameter("Codespace 名称", Required = true)] string codespace_name,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Delete, $"user/codespaces/{codespace_name}", ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok($"已删除 Codespace {codespace_name}") : Fail(result.Error);
    }

    /// <summary>
    /// 在 VS Code 中打开 Codespace — 简化提示（需 gh cs code CLI 交互）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhCodespaceCode, "在 VS Code 中打开 Codespace(提示用系统 gh)", "github", ConcurrencySafe = true)]
    public Task<ToolResult> GhCodespaceCodeAsync(
        [McpToolParameter("Codespace 名称(可选,默认最新)", Required = false)] string? codespace_name = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Ok($"请在终端运行: gh cs code{(string.IsNullOrWhiteSpace(codespace_name) ? "" : $" -c {codespace_name}")}\n（VS Code 打开 Codespace 需要 gh CLI 交互，无法通过 API 完成）"));

    /// <summary>
    /// SSH 到 Codespace — 简化提示（需 gh cs ssh CLI 交互）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhCodespaceSsh, "SSH 到 Codespace(提示用系统 gh)", "github", ConcurrencySafe = true)]
    public Task<ToolResult> GhCodespaceSshAsync(
        [McpToolParameter("Codespace 名称(可选,默认最新)", Required = false)] string? codespace_name = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Ok($"请在终端运行: gh cs ssh{(string.IsNullOrWhiteSpace(codespace_name) ? "" : $" -c {codespace_name}")}\n（SSH 到 Codespace 需要 gh CLI 交互，无法通过 API 完成）"));

    // === Discussion（GraphQL）===

    /// <summary>
    /// 列出 Discussion — 调 GraphQL 查询 repository.discussions
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhDiscussionList, "列出 Discussion(GraphQL)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhDiscussionListAsync(
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var first = Math.Min(limit ?? 30, 100);
            var graphql = BuildGraphQL($"query{{repository(owner:\"{owner}\",name:\"{repoName}\"){{discussions(first:{first}){{nodes{{number title author{{login}} category{{name}} createdAt}}}}}}}}");
            var result = await client.SendAsync(HttpMethod.Post, "graphql", graphql, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(SummarizeDiscussionList(result.Body));
        }).ConfigureAwait(false);

    /// <summary>精简 Discussion 列表 — 表格格式(number, title, author, category)</summary>
    private static string SummarizeDiscussionList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder(256);
            var nodes = doc.RootElement.GetProperty("data").GetProperty("repository").GetProperty("discussions").GetProperty("nodes");
            sb.AppendLine("编号\t标题\t作者\t分类\t创建时间");
            foreach (var d in nodes.EnumerateArray()) {
                var number = d.TryGetProperty("number", out var nEl) ? nEl.GetInt32() : 0;
                var title = d.TryGetProperty("title", out var tEl) ? tEl.GetString() ?? "" : "";
                var author = d.TryGetProperty("author", out var aEl) && aEl.TryGetProperty("login", out var lEl) ? lEl.GetString() ?? "" : "";
                var category = d.TryGetProperty("category", out var cEl) && cEl.TryGetProperty("name", out var cnEl) ? cnEl.GetString() ?? "" : "";
                var createdAt = d.TryGetProperty("createdAt", out var caEl) ? caEl.GetString() ?? "" : "";
                sb.AppendLine($"{number}\t{title}\t{author}\t{category}\t{createdAt}");
            }
            return sb.ToString();
        } catch (Exception ex) { return $"解析 Discussion 列表失败: {ex.Message}"; }
    }

    /// <summary>
    /// 查看 Discussion 详情 — 调 GraphQL 查询 repository.discussion(number)
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhDiscussionView, "查看 Discussion 详情(GraphQL)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhDiscussionViewAsync(
        [McpToolParameter("Discussion 编号", Required = true)] int number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var graphql = BuildGraphQL($"query{{repository(owner:\"{owner}\",name=\"{repoName}\"){{discussion(number:{number}){{number title body author{{login}} category{{name}} createdAt url}}}}}}");
            var result = await client.SendAsync(HttpMethod.Post, "graphql", graphql, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok(result.Body) : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 创建 Discussion — 调 GraphQL mutation createDiscussion，需 discussion category ID
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhDiscussionCreate, "创建 Discussion(GraphQL,需分类)", "github")]
    public async Task<ToolResult> GhDiscussionCreateAsync(
        [McpToolParameter("标题", Required = true)] string title,
        [McpToolParameter("正文", Required = true)] string body,
        [McpToolParameter("分类名(如 General/Q&A/Ideas)", Required = true)] string category,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var catQuery = BuildGraphQL($"query{{repository(owner:\"{owner}\",name=\"{repoName}\"){{discussionCategories(first:50){{nodes{{id name}}}}}}}}");
            var catResult = await client.SendAsync(HttpMethod.Post, "graphql", catQuery, ct: cancellationToken).ConfigureAwait(false);
            if (!catResult.Success) return Fail(catResult.Error);
            string? categoryId = null;
            try {
                using var catDoc = JsonDocument.Parse(catResult.Body);
                var cats = catDoc.RootElement.GetProperty("data").GetProperty("repository").GetProperty("discussionCategories").GetProperty("nodes");
                foreach (var c in cats.EnumerateArray()) {
                    if (c.TryGetProperty("name", out var nEl) && nEl.GetString() == category) {
                        categoryId = c.GetProperty("id").GetString();
                        break;
                    }
                }
            } catch (Exception ex) { return Fail($"解析分类失败: {ex.Message}"); }
            if (categoryId is null) return Fail($"未找到分类 '{category}'");
            var repoIdQuery = BuildGraphQL($"query{{repository(owner:\"{owner}\",name:\"{repoName}\"){{id}}}}");
            var repoIdResult = await client.SendAsync(HttpMethod.Post, "graphql", repoIdQuery, ct: cancellationToken).ConfigureAwait(false);
            if (!repoIdResult.Success) return Fail(repoIdResult.Error);
            string? repoId = null;
            try {
                using var ridDoc = JsonDocument.Parse(repoIdResult.Body);
                repoId = ridDoc.RootElement.GetProperty("data").GetProperty("repository").GetProperty("id").GetString();
            } catch (Exception ex) { return Fail($"解析仓库 ID 失败: {ex.Message}"); }
            if (repoId is null) return Fail("无法获取仓库 node_id");
            var escapedTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var escapedBody = body.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var mutation = BuildGraphQL($"mutation{{createDiscussion(input:{{repositoryId:\"{repoId}\",categoryId:\"{categoryId}\",title:\"{escapedTitle}\",body:\"{escapedBody}\"}}){{discussion{{number url}}}}}}");
            var result = await client.SendAsync(HttpMethod.Post, "graphql", mutation, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已创建 Discussion: {title}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 编辑 Discussion — 调 GraphQL mutation updateDiscussion
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhDiscussionEdit, "编辑 Discussion(GraphQL)", "github")]
    public async Task<ToolResult> GhDiscussionEditAsync(
        [McpToolParameter("Discussion 编号", Required = true)] int number,
        [McpToolParameter("新标题(可选)", Required = false)] string? title = null,
        [McpToolParameter("新正文(可选)", Required = false)] string? body = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var idQuery = BuildGraphQL($"query{{repository(owner:\"{owner}\",name=\"{repoName}\"){{discussion(number:{number}){{id}}}}}}");
            var idResult = await client.SendAsync(HttpMethod.Post, "graphql", idQuery, ct: cancellationToken).ConfigureAwait(false);
            if (!idResult.Success) return Fail(idResult.Error);
            string? discussionId = null;
            try {
                using var idDoc = JsonDocument.Parse(idResult.Body);
                discussionId = idDoc.RootElement.GetProperty("data").GetProperty("repository").GetProperty("discussion").GetProperty("id").GetString();
            } catch (Exception ex) { return Fail($"解析 Discussion ID 失败: {ex.Message}"); }
            if (discussionId is null) return Fail("无法获取 Discussion node_id");
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(title)) parts.Add($"title:\"{title.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
            if (!string.IsNullOrWhiteSpace(body)) parts.Add($"body:\"{body.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
            if (parts.Count == 0) return Fail("需要 title 或 body 至少一个");
            var inputFields = string.Join(",", parts);
            var mutation = BuildGraphQL($"mutation{{updateDiscussion(input:{{discussionId:\"{discussionId}\",{inputFields}}}){{discussion{{number url}}}}}}");
            var result = await client.SendAsync(HttpMethod.Post, "graphql", mutation, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已编辑 Discussion #{number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 评论 Discussion — 调 GraphQL mutation addDiscussionComment
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhDiscussionComment, "评论 Discussion(GraphQL)", "github")]
    public async Task<ToolResult> GhDiscussionCommentAsync(
        [McpToolParameter("Discussion 编号", Required = true)] int number,
        [McpToolParameter("评论内容", Required = true)] string body,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var idQuery = BuildGraphQL($"query{{repository(owner:\"{owner}\",name=\"{repoName}\"){{discussion(number:{number}){{id}}}}}}");
            var idResult = await client.SendAsync(HttpMethod.Post, "graphql", idQuery, ct: cancellationToken).ConfigureAwait(false);
            if (!idResult.Success) return Fail(idResult.Error);
            string? discussionId = null;
            try {
                using var idDoc = JsonDocument.Parse(idResult.Body);
                discussionId = idDoc.RootElement.GetProperty("data").GetProperty("repository").GetProperty("discussion").GetProperty("id").GetString();
            } catch (Exception ex) { return Fail($"解析 Discussion ID 失败: {ex.Message}"); }
            if (discussionId is null) return Fail("无法获取 Discussion node_id");
            var escapedBody = body.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var mutation = BuildGraphQL($"mutation{{addDiscussionComment(input:{{discussionId:\"{discussionId}\",body:\"{escapedBody}\"}}){{comment{{id}}}}}}");
            var result = await client.SendAsync(HttpMethod.Post, "graphql", mutation, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已评论 Discussion #{number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    // === Project（GraphQL）===

    /// <summary>
    /// 列出 Project — 调 GraphQL 查询 user.projectsV2 或 organization.projectsV2
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhProjectList, "列出 Project(GraphQL v2)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhProjectListAsync(
        [McpToolParameter("组织名(可选,列出组织 Project,默认当前用户)", Required = false)] string? org = null,
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var first = Math.Min(limit ?? 30, 100);
        string graphql;
        if (!string.IsNullOrWhiteSpace(org))
            graphql = BuildGraphQL($"query{{organization(login:\"{org}\"){{projectsV2(first:{first}){{nodes{{number title url closed state}}}}}}}}");
        else
            graphql = BuildGraphQL($"query{{viewer{{projectsV2(first:{first}){{nodes{{number title url closed state}}}}}}}}");
        var result = await _apiClient.SendAsync(HttpMethod.Post, "graphql", graphql, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeProjectList(result.Body));
    }

    /// <summary>精简 Project 列表 — 表格格式(number, title, state, url)</summary>
    private static string SummarizeProjectList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder(256);
            var data = doc.RootElement.GetProperty("data");
            var root = data.TryGetProperty("organization", out var orgEl) ? orgEl : data.GetProperty("viewer");
            var nodes = root.GetProperty("projectsV2").GetProperty("nodes");
            sb.AppendLine("编号\t标题\t状态\tURL");
            foreach (var p in nodes.EnumerateArray()) {
                var number = p.TryGetProperty("number", out var nEl) ? nEl.GetInt32() : 0;
                var title = p.TryGetProperty("title", out var tEl) ? tEl.GetString() ?? "" : "";
                var state = p.TryGetProperty("state", out var sEl) ? sEl.GetString() ?? "" : "";
                var url = p.TryGetProperty("url", out var uEl) ? uEl.GetString() ?? "" : "";
                sb.AppendLine($"{number}\t{title}\t{state}\t{url}");
            }
            return sb.ToString();
        } catch (Exception ex) { return $"解析 Project 列表失败: {ex.Message}"; }
    }

    /// <summary>
    /// 查看 Project 详情 — 调 GraphQL 查询 organization.projectsV2(number)
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhProjectView, "查看 Project 详情(GraphQL v2)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhProjectViewAsync(
        [McpToolParameter("Project 编号", Required = true)] int number,
        [McpToolParameter("组织名(可选,默认当前用户)", Required = false)] string? org = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        string graphql;
        if (!string.IsNullOrWhiteSpace(org))
            graphql = BuildGraphQL($"query{{organization(login:\"{org}\"){{projectV2(number:{number}){{title url closed state items(first:20){{nodes{{content{{...on Issue{{number title}} ...on PullRequest{{number title}}}}}}}}}}}}}}");
        else
            graphql = BuildGraphQL($"query{{viewer{{projectV2(number:{number}){{title url closed state items(first:20){{nodes{{content{{...on Issue{{number title}} ...on PullRequest{{number title}}}}}}}}}}}}}}");
        var result = await _apiClient.SendAsync(HttpMethod.Post, "graphql", graphql, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok(result.Body) : Fail(result.Error);
    }

    /// <summary>
    /// 创建 Project — 调 GraphQL mutation createProjectV2
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhProjectCreate, "创建 Project(GraphQL v2)", "github")]
    public async Task<ToolResult> GhProjectCreateAsync(
        [McpToolParameter("标题", Required = true)] string title,
        [McpToolParameter("组织名(可选,在组织下创建,默认当前用户)", Required = false)] string? org = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var escapedTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string graphql;
        if (!string.IsNullOrWhiteSpace(org))
            graphql = BuildGraphQL($"mutation{{createProjectV2(input:{{ownerId:\"{org}\",title:\"{escapedTitle}\"}}){{projectV2{{number url}}}}}}");
        else {
            var viewerQuery = BuildGraphQL("query{viewer{id}}");
            var viewerResult = await _apiClient.SendAsync(HttpMethod.Post, "graphql", viewerQuery, ct: cancellationToken).ConfigureAwait(false);
            if (!viewerResult.Success) return Fail(viewerResult.Error);
            string? viewerId = null;
            try {
                using var vDoc = JsonDocument.Parse(viewerResult.Body);
                viewerId = vDoc.RootElement.GetProperty("data").GetProperty("viewer").GetProperty("id").GetString();
            } catch (Exception ex) { return Fail($"解析用户 ID 失败: {ex.Message}"); }
            if (viewerId is null) return Fail("无法获取用户 node_id");
            graphql = BuildGraphQL($"mutation{{createProjectV2(input:{{ownerId:\"{viewerId}\",title:\"{escapedTitle}\"}}){{projectV2{{number url}}}}}}");
        }
        var result = await _apiClient.SendAsync(HttpMethod.Post, "graphql", graphql, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? OkBrief(result.Body, $"已创建 Project: {title}") : Fail(result.Error);
    }

    /// <summary>
    /// 删除 Project — 调 GraphQL mutation deleteProjectV2
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhProjectDelete, "删除 Project(GraphQL v2)", "github")]
    public async Task<ToolResult> GhProjectDeleteAsync(
        [McpToolParameter("Project 编号", Required = true)] int number,
        [McpToolParameter("组织名(可选,默认当前用户)", Required = false)] string? org = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        string idQuery;
        if (!string.IsNullOrWhiteSpace(org))
            idQuery = BuildGraphQL($"query{{organization(login:\"{org}\"){{projectV2(number:{number}){{id}}}}}}");
        else
            idQuery = BuildGraphQL($"query{{viewer{{projectV2(number:{number}){{id}}}}}}");
        var idResult = await _apiClient.SendAsync(HttpMethod.Post, "graphql", idQuery, ct: cancellationToken).ConfigureAwait(false);
        if (!idResult.Success) return Fail(idResult.Error);
        string? projectId = null;
        try {
            using var idDoc = JsonDocument.Parse(idResult.Body);
            var data = idDoc.RootElement.GetProperty("data");
            var root = data.TryGetProperty("organization", out var orgEl) ? orgEl : data.GetProperty("viewer");
            projectId = root.GetProperty("projectV2").GetProperty("id").GetString();
        } catch (Exception ex) { return Fail($"解析 Project ID 失败: {ex.Message}"); }
        if (projectId is null) return Fail("无法获取 Project node_id");
        var mutation = BuildGraphQL($"mutation{{deleteProjectV2(input:{{projectId:\"{projectId}\"}}){{projectV2{{number}}}}}}");
        var result = await _apiClient.SendAsync(HttpMethod.Post, "graphql", mutation, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok($"已删除 Project #{number}") : Fail(result.Error);
    }

    /// <summary>
    /// 编辑 Project — 调 GraphQL mutation updateProjectV2
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhProjectEdit, "编辑 Project(GraphQL v2)", "github")]
    public async Task<ToolResult> GhProjectEditAsync(
        [McpToolParameter("Project 编号", Required = true)] int number,
        [McpToolParameter("新标题(可选)", Required = false)] string? title = null,
        [McpToolParameter("新描述(可选)", Required = false)] string? description = null,
        [McpToolParameter("组织名(可选,默认当前用户)", Required = false)] string? org = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        string idQuery;
        if (!string.IsNullOrWhiteSpace(org))
            idQuery = BuildGraphQL($"query{{organization(login:\"{org}\"){{projectV2(number:{number}){{id}}}}}}");
        else
            idQuery = BuildGraphQL($"query{{viewer{{projectV2(number:{number}){{id}}}}}}");
        var idResult = await _apiClient.SendAsync(HttpMethod.Post, "graphql", idQuery, ct: cancellationToken).ConfigureAwait(false);
        if (!idResult.Success) return Fail(idResult.Error);
        string? projectId = null;
        try {
            using var idDoc = JsonDocument.Parse(idResult.Body);
            var data = idDoc.RootElement.GetProperty("data");
            var root = data.TryGetProperty("organization", out var orgEl) ? orgEl : data.GetProperty("viewer");
            projectId = root.GetProperty("projectV2").GetProperty("id").GetString();
        } catch (Exception ex) { return Fail($"解析 Project ID 失败: {ex.Message}"); }
        if (projectId is null) return Fail("无法获取 Project node_id");
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title)) parts.Add($"title:\"{title.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrWhiteSpace(description)) parts.Add($"shortDescription:\"{description.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
        if (parts.Count == 0) return Fail("需要 title 或 description 至少一个");
        var inputFields = string.Join(",", parts);
        var mutation = BuildGraphQL($"mutation{{updateProjectV2(input:{{projectId:\"{projectId}\",{inputFields}}}){{projectV2{{number url}}}}}}");
        var result = await _apiClient.SendAsync(HttpMethod.Post, "graphql", mutation, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? OkBrief(result.Body, $"已编辑 Project #{number}") : Fail(result.Error);
    }

    /// <summary>
    /// 关闭 Project — 调 GraphQL mutation updateProjectV2(closed=true)
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhProjectClose, "关闭 Project(GraphQL v2)", "github")]
    public async Task<ToolResult> GhProjectCloseAsync(
        [McpToolParameter("Project 编号", Required = true)] int number,
        [McpToolParameter("组织名(可选,默认当前用户)", Required = false)] string? org = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        string idQuery;
        if (!string.IsNullOrWhiteSpace(org))
            idQuery = BuildGraphQL($"query{{organization(login:\"{org}\"){{projectV2(number:{number}){{id}}}}}}");
        else
            idQuery = BuildGraphQL($"query{{viewer{{projectV2(number:{number}){{id}}}}}}");
        var idResult = await _apiClient.SendAsync(HttpMethod.Post, "graphql", idQuery, ct: cancellationToken).ConfigureAwait(false);
        if (!idResult.Success) return Fail(idResult.Error);
        string? projectId = null;
        try {
            using var idDoc = JsonDocument.Parse(idResult.Body);
            var data = idDoc.RootElement.GetProperty("data");
            var root = data.TryGetProperty("organization", out var orgEl) ? orgEl : data.GetProperty("viewer");
            projectId = root.GetProperty("projectV2").GetProperty("id").GetString();
        } catch (Exception ex) { return Fail($"解析 Project ID 失败: {ex.Message}"); }
        if (projectId is null) return Fail("无法获取 Project node_id");
        var mutation = BuildGraphQL($"mutation{{updateProjectV2(input:{{projectId:\"{projectId}\",closed:true}}){{projectV2{{number url}}}}}}");
        var result = await _apiClient.SendAsync(HttpMethod.Post, "graphql", mutation, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? OkBrief(result.Body, $"已关闭 Project #{number}") : Fail(result.Error);
    }

    // === Alias（本地配置，提示用系统 gh）===

    /// <summary>
    /// 列出别名 — 读写 gh config.yml 中 aliases 部分
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhAliasList, "列出别名(读写 config.yml)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhAliasListAsync(CancellationToken cancellationToken = default) {
        var configPath = GetGhConfigPath(false);
        if (!_fs.FileExists(configPath)) return Ok("（无别名：配置文件不存在）");
        var content = await _fs.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var aliases = ParseYamlSection(content, "aliases");
        if (aliases.Count == 0) return Ok("（无别名）");
        var sb = new StringBuilder(64);
        foreach (var (key, value) in aliases) sb.AppendLine($"{key}: {value}");
        return Ok(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// 设置别名 — 读写 gh config.yml 中 aliases 部分
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhAliasSet, "设置别名(读写 config.yml)", "github")]
    public async Task<ToolResult> GhAliasSetAsync(
        [McpToolParameter("别名名", Required = true)] string alias,
        [McpToolParameter("命令内容", Required = true)] string command,
        [McpToolParameter("是否保存到 shell(可选)", Required = false)] bool? shell = null,
        CancellationToken cancellationToken = default) {
        var configPath = GetGhConfigPath(false);
        if (!_fs.FileExists(configPath)) return Fail($"gh 配置文件不存在: {configPath} — 请用系统 gh CLI 登录: gh auth login");
        var content = await _fs.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var updated = SetYamlSectionValue(content, "aliases", alias, shell == true ? $"!shell {command}" : command);
        await _fs.WriteAllTextAsync(configPath, updated, cancellationToken).ConfigureAwait(false);
        return Ok($"已设置别名 {alias} = {command}");
    }

    /// <summary>
    /// 删除别名 — 读写 gh config.yml 中 aliases 部分
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhAliasDelete, "删除别名(读写 config.yml)", "github")]
    public async Task<ToolResult> GhAliasDeleteAsync(
        [McpToolParameter("别名名", Required = true)] string alias,
        CancellationToken cancellationToken = default) {
        var configPath = GetGhConfigPath(false);
        if (!_fs.FileExists(configPath)) return Fail($"gh 配置文件不存在: {configPath} — 请用系统 gh CLI 登录: gh auth login");
        var content = await _fs.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var updated = DeleteYamlSectionValue(content, "aliases", alias);
        if (updated == content) return Fail($"别名不存在: {alias}");
        await _fs.WriteAllTextAsync(configPath, updated, cancellationToken).ConfigureAwait(false);
        return Ok($"已删除别名 {alias}");
    }

    // === Extension（list 扫描本地目录，install/upgrade/remove 提示用系统 gh）===

    /// <summary>
    /// 列出已安装扩展 — 扫描本地 gh extensions 目录
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhExtensionList, "列出已安装扩展(扫描本地目录)", "github", ConcurrencySafe = true)]
    public Task<ToolResult> GhExtensionListAsync(CancellationToken cancellationToken = default) {
        var extDir = GetGhExtensionsPath();
        if (!_fs.DirectoryExists(extDir)) return Task.FromResult(Ok("（无已安装扩展）"));
        var extensions = _fs.EnumerateDirectories(extDir, "gh-*", SearchOption.TopDirectoryOnly).ToList();
        if (extensions.Count == 0) return Task.FromResult(Ok("（无已安装扩展）"));
        var sb = new StringBuilder(64);
        foreach (var dir in extensions) sb.AppendLine(_fs.GetDirectoryName(dir));
        return Task.FromResult(Ok(sb.ToString().TrimEnd()));
    }

    /// <summary>
    /// 获取 gh extensions 目录路径
    /// </summary>
    internal static string GetGhExtensionsPath() {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData) && OperatingSystem.IsWindows()) return Path.Combine(localAppData, "gh", "extensions");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "gh", "extensions");
    }

    /// <summary>
    /// 安装扩展 — 提示用系统 gh CLI
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhExtensionInstall, "安装扩展(提示用系统 gh)", "github")]
    public Task<ToolResult> GhExtensionInstallAsync(
        [McpToolParameter("扩展名(如 owner/gh-ext)", Required = true)] string extension,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Ok($"请在终端运行: gh extension install {extension}\n（扩展安装为本地操作，需 gh CLI 直接执行）"));

    /// <summary>
    /// 升级扩展 — 提示用系统 gh CLI
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhExtensionUpgrade, "升级扩展(提示用系统 gh)", "github")]
    public Task<ToolResult> GhExtensionUpgradeAsync(
        [McpToolParameter("扩展名(可选,默认全部)", Required = false)] string? extension = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Ok($"请在终端运行: gh extension upgrade{(string.IsNullOrWhiteSpace(extension) ? " --all" : $" {extension}")}\n（扩展升级为本地操作，需 gh CLI 直接执行）"));

    /// <summary>
    /// 移除扩展 — 提示用系统 gh CLI
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhExtensionRemove, "移除扩展(提示用系统 gh)", "github")]
    public Task<ToolResult> GhExtensionRemoveAsync(
        [McpToolParameter("扩展名", Required = true)] string extension,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Ok($"请在终端运行: gh extension remove {extension}\n（扩展移除为本地操作，需 gh CLI 直接执行）"));

    // === Licenses（第三方许可证）===

    /// <summary>
    /// 查看可用许可证列表 — 调 REST API GET /licenses 获取 SPDX 许可证列表
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhLicenses, "查看可用开源许可证列表(API)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhLicensesAsync(CancellationToken cancellationToken = default) {
        if (_apiClient is null) return Fail("GitHub REST API 客户端未配置(IGitHubApiClient 未注入)");
        var result = await _apiClient.SendAsync(HttpMethod.Get, "licenses", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(SummarizeLicenses(result.Body));
    }

    /// <summary>
    /// 精简许可证列表 JSON — 只保留 key/name/spdx_id，便于浏览
    /// </summary>
    private static string SummarizeLicenses(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var sb = new StringBuilder(256);
            foreach (var license in doc.RootElement.EnumerateArray()) {
                var key = license.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                var name = license.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var spdx = license.TryGetProperty("spdx_id", out var s) ? s.GetString() ?? "" : "";
                sb.AppendLine($"{key} | {spdx} | {name}");
            }
            return sb.ToString().TrimEnd();
        } catch { return json; }
    }
}
