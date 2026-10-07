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
        [McpToolParameter("分支名(可选,web=true 时 URL 带分支)", Required = false)] string? branch = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        [McpToolParameter("输出档位(0=gh风格简洁[默认] 1=精简JSON 2=完整JSON[从缓存读])", Required = false)] int? verbosity = null,
        [McpToolParameter("web=true 只返回仓库浏览器 URL", Required = false)] bool? web = null,
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 name,full_name,description)", Required = false)] string? json_fields = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (web == true) {
                var repoResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}", ct: cancellationToken).ConfigureAwait(false);
                if (!repoResult.Success) return Fail(repoResult.Error);
                var url = ExtractHtmlUrl(repoResult.Body);
                if (string.IsNullOrEmpty(url)) return Fail("无法从仓库响应中解析 html_url");
                if (!string.IsNullOrWhiteSpace(branch)) url += $"/tree/{branch}";
                return Ok(url);
            }
            var cacheKey = BuildGhCacheKey("gh_repo_view", $"{owner}/{repoName}");
            return await GetOrFetchWithCacheAsync(client, cacheKey, $"repos/{owner}/{repoName}", verbosity, json_fields, SummarizeRepo, "name,full_name,description,language,default_branch,html_url", cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

    /// <summary>
    /// 克隆仓库 — 走本地 git 命令，支持 depth/bare/single-branch/filter/sparse
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoClone, "克隆仓库(支持 depth/bare/single-branch/filter/sparse)", "github")]
    public async Task<ToolResult> GhRepoCloneAsync(
        [McpToolParameter("仓库名(owner/repo 或 URL)", Required = true)] string repo,
        [McpToolParameter("克隆目标目录(可选)", Required = false)] string? dir = null,
        [McpToolParameter("浅克隆深度(可选,如 1)", Required = false)] int? depth = null,
        [McpToolParameter("裸克隆(默认 false)", Required = false)] bool? bare = null,
        [McpToolParameter("只克隆默认分支(默认 false)", Required = false)] bool? single_branch = null,
        [McpToolParameter("部分克隆过滤器(可选,如 blob:none)", Required = false)] string? filter = null,
        [McpToolParameter("稀疏检出(默认 false)", Required = false)] bool? sparse = null,
        [McpToolParameter("上游 remote 名(可选,默认 origin)", Required = false)] string? upstream_remote_name = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_git is null) return Fail("git 命令执行器未配置（IGitCommandRunner 未注入）");

        var cloneUrl = repo.StartsWith("http", StringComparison.OrdinalIgnoreCase) || repo.Contains('@')
            ? repo
            : $"https://github.com/{repo}.git";

        var sb = new StringBuilder("clone");
        if (bare == true) sb.Append(" --bare");
        if (single_branch == true) sb.Append(" --single-branch");
        if (depth is > 0) sb.Append($" --depth={depth}");
        if (!string.IsNullOrWhiteSpace(filter)) sb.Append($" --filter={filter}");
        if (sparse == true) sb.Append(" --sparse");
        sb.Append($" {cloneUrl}");
        if (!string.IsNullOrWhiteSpace(dir)) sb.Append($" {dir}");

        var result = await _git.ExecuteAsync(sb.ToString(), working_dir, cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);

        if (!string.IsNullOrWhiteSpace(upstream_remote_name) && upstream_remote_name != "origin" && bare != true) {
            var remoteResult = await _git.ExecuteAsync($"remote rename origin {upstream_remote_name}", working_dir, cancellationToken).ConfigureAwait(false);
            if (!remoteResult.Success) return Ok(result.Output, $"已克隆 {repo}（但重命名 remote 失败: {remoteResult.Error}）");
        }

        return result.Success ? Ok(result.Output, $"已克隆 {repo}") : Fail(result.Error);
    }

    /// <summary>
    /// 创建仓库 — 支持 public/private/internal 可见性、描述、README 初始化、homepage、gitignore/license 模板、template/clone/source/push/disable-issues/disable-wiki
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoCreate, "创建仓库(public/private/internal,支持 template/clone/source/push/disable-issues/disable-wiki)", "github")]
    public async Task<ToolResult> GhRepoCreateAsync(
        [McpToolParameter("仓库名", Required = true)] string name,
        [McpToolParameter("可见性(public/private/internal,默认 private)", Required = false)] string? visibility = null,
        [McpToolParameter("描述(可选)", Required = false)] string? description = null,
        [McpToolParameter("是否添加 README(可选)", Required = false)] bool? add_readme = null,
        [McpToolParameter("主页 URL(可选)", Required = false)] string? homepage = null,
        [McpToolParameter("gitignore 模板(可选,如 VisualStudio)", Required = false)] string? gitignore = null,
        [McpToolParameter("license 模板(可选,如 mit)", Required = false)] string? license = null,
        [McpToolParameter("模板仓库(owner/repo,可选,用模板创建)", Required = false)] string? template = null,
        [McpToolParameter("模板创建时包含所有分支(可选,默认只复制默认分支)", Required = false)] bool? include_all_branches = null,
        [McpToolParameter("组织名(可选,在组织下创建)", Required = false)] string? org = null,
        [McpToolParameter("团队名(可选,组织仓库添加到团队)", Required = false)] string? team = null,
        [McpToolParameter("创建后克隆到本地(默认 false)", Required = false)] bool? clone = null,
        [McpToolParameter("本地源目录(可选,将该目录初始化为 git 并添加 remote;空字符串则用 working_dir)", Required = false)] string? source = null,
        [McpToolParameter("推送本地提交到远程(source 非空时生效)", Required = false)] bool? push = null,
        [McpToolParameter("禁用 Issues(可选)", Required = false)] bool? disable_issues = null,
        [McpToolParameter("禁用 Wiki(可选)", Required = false)] bool? disable_wiki = null,
        [McpToolParameter("web=true 只返回仓库浏览器 URL", Required = false)] bool? web = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var vis = string.IsNullOrWhiteSpace(visibility) ? "private" : visibility;
        var isPrivate = vis.Equals("private", StringComparison.OrdinalIgnoreCase);
        var isInternal = vis.Equals("internal", StringComparison.OrdinalIgnoreCase);

        string jsonBody;
        string createPath;
        if (!string.IsNullOrWhiteSpace(template)) {
            var parsed = ParseGitHubRepoRef(template);
            if (parsed is null) return Fail("模板仓库格式错误，应为 owner/repo");
            createPath = $"repos/{parsed.Value.owner}/{parsed.Value.repo}/generate";
            jsonBody = JsonSerializer.Serialize(new RepoTemplateGenerateRequest {
                Name = name,
                Description = description,
                Private = isPrivate || isInternal,
                Visibility = isInternal ? "internal" : (isPrivate ? "private" : "public"),
                IncludeAllBranches = include_all_branches
            }, GitHubApiJsonContext.Safe.RepoTemplateGenerateRequest);
        } else {
            createPath = !string.IsNullOrWhiteSpace(org) ? $"orgs/{org}/repos" : "user/repos";
            jsonBody = JsonSerializer.Serialize(new RepoCreateRequest {
                Name = name,
                Private = isPrivate || isInternal,
                Visibility = isInternal ? "internal" : null,
                Description = description,
                AutoInit = add_readme,
                Homepage = homepage,
                GitignoreTemplate = gitignore,
                LicenseTemplate = license,
                HasIssues = disable_issues == true ? false : null,
                HasWiki = disable_wiki == true ? false : null
            }, GitHubApiJsonContext.Safe.RepoCreateRequest);
        }

        var result = await _apiClient.SendAsync(HttpMethod.Post, createPath, jsonBody, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);

        var repoFullName = !string.IsNullOrWhiteSpace(org) ? $"{org}/{name}" : name;
        if (!string.IsNullOrWhiteSpace(org) && !string.IsNullOrWhiteSpace(team)) {
            var teamResult = await _apiClient.SendAsync(HttpMethod.Put, $"orgs/{org}/teams/{team}/repos/{org}/{name}", ct: cancellationToken).ConfigureAwait(false);
            if (!teamResult.Success) return OkBrief(result.Body, $"已创建仓库 {repoFullName}（但添加到团队失败: {teamResult.Error}）");
        }

        if (web == true) {
            var url = ExtractHtmlUrl(result.Body);
            return string.IsNullOrEmpty(url) ? OkBrief(result.Body, $"已创建仓库 {repoFullName}") : Ok(url);
        }

        if (clone == true) {
            if (_git is null) return OkBrief(result.Body, $"已创建仓库 {repoFullName}（但未克隆：git 未配置）");
            var cloneResult = await _git.ExecuteAsync($"clone https://github.com/{repoFullName}.git", working_dir, cancellationToken).ConfigureAwait(false);
            if (!cloneResult.Success) return OkBrief(result.Body, $"已创建仓库 {repoFullName}（但克隆失败: {cloneResult.Error}）");
        }

        if (source is not null) {
            var sourceDir = source.Length == 0 ? (working_dir ?? ".") : source;
            var sourceMsg = await InitSourceRepoAsync(sourceDir, repoFullName, push == true, cancellationToken).ConfigureAwait(false);
            return OkBrief(result.Body, $"已创建仓库 {repoFullName}{sourceMsg}");
        }

        return OkBrief(result.Body, $"已创建仓库 {repoFullName}");
    }

    /// <summary>
    /// 将本地目录初始化为 git 仓库并添加 remote，可选推送 — 用于 gh repo create --source [--push]
    /// </summary>
    /// <param name="sourceDir">本地源目录路径</param>
    /// <param name="repoFullName">远程仓库全名(owner/repo)</param>
    /// <param name="push">是否推送本地提交到远程</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果描述（含失败原因），用于拼接在"已创建仓库"消息后</returns>
    private async Task<string> InitSourceRepoAsync(string sourceDir, string repoFullName, bool push, CancellationToken cancellationToken) {
        if (_git is null) return "（但 source 初始化失败：git 未配置）";
        var remoteUrl = $"https://github.com/{repoFullName}.git";
        var hasGit = _fs.DirectoryExists(Path.Combine(sourceDir, ".git"));
        if (!hasGit) {
            var initResult = await _git.ExecuteAsync("init", sourceDir, cancellationToken).ConfigureAwait(false);
            if (!initResult.Success) return $"（但 source 初始化失败: {initResult.Error}）";
        }
        var remoteResult = await _git.ExecuteAsync($"remote add origin {remoteUrl}", sourceDir, cancellationToken).ConfigureAwait(false);
        if (!remoteResult.Success && !remoteResult.Error.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            return $"（但添加 remote 失败: {remoteResult.Error}）";
        if (!push) return "（已初始化本地 git 并添加 remote）";
        var pushResult = await _git.ExecuteAsync("push -u origin HEAD", sourceDir, cancellationToken).ConfigureAwait(false);
        return pushResult.Success ? "（已推送本地提交）" : $"（但推送失败: {pushResult.Error}）";
    }

    /// <summary>
    /// Fork 仓库 — 调 REST API 创建 Fork，可选克隆到本地、指定组织、fork-name、default-branch-only
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoFork, "Fork 仓库(可选指定组织/fork-name/default-branch-only)", "github")]
    public async Task<ToolResult> GhRepoForkAsync(
        [McpToolParameter("仓库名(owner/repo)", Required = true)] string repo,
        [McpToolParameter("是否克隆到本地(默认 false)", Required = false)] bool? clone = null,
        [McpToolParameter("Fork 到指定组织(可选)", Required = false)] string? org = null,
        [McpToolParameter("Fork 仓库名(可选,默认同原名)", Required = false)] string? fork_name = null,
        [McpToolParameter("只 fork 默认分支(可选)", Required = false)] bool? default_branch_only = null,
        [McpToolParameter("本地 remote 名(可选,clone=true 时添加)", Required = false)] string? remote = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var parsed = ParseGitHubRepoRef(repo);
        if (parsed is null) return Fail("仓库名格式错误，应为 owner/repo");
        var (owner, repoName) = parsed.Value;

        var forkBody = JsonSerializer.Serialize(new RepoForkRequest {
            Organization = org,
            Name = fork_name,
            DefaultBranchOnly = default_branch_only
        }, GitHubApiJsonContext.Safe.RepoForkRequest);
        var result = await _apiClient.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/forks", forkBody, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);

        if (clone != true) return OkBrief(result.Body, $"已 Fork {repo}");
        if (_git is null) return OkBrief(result.Body, $"已 Fork {repo}（但未克隆：git 未配置）");
        var forkFullName = !string.IsNullOrWhiteSpace(org)
            ? $"{org}/{fork_name ?? repoName}"
            : fork_name is not null ? $"{owner}/{fork_name}" : repo;
        var cloneResult = await _git.ExecuteAsync($"clone https://github.com/{forkFullName}.git", working_dir, cancellationToken).ConfigureAwait(false);
        if (!cloneResult.Success) return OkBrief(result.Body, $"已 Fork {repo}（但克隆失败: {cloneResult.Error}）");
        if (string.IsNullOrWhiteSpace(remote) || remote == "origin") return OkBrief(result.Body, $"已 Fork {repo}");
        var remoteResult = await _git.ExecuteAsync($"remote add {remote} https://github.com/{repo}.git", working_dir, cancellationToken).ConfigureAwait(false);
        return remoteResult.Success
            ? OkBrief(result.Body, $"已 Fork+克隆 {repo}（remote: {remote}）")
            : OkBrief(result.Body, $"已 Fork+克隆 {repo}（但添加 remote 失败: {remoteResult.Error}）");
    }

    /// <summary>
    /// 列出自己可访问的仓库 — 支持语言/可见性/source/fork/archived/topic 过滤，调 REST API 获取仓库列表，精简输出
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoList, "列出自己可访问的仓库(支持语言/可见性/source/fork/archived/topic 过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRepoListAsync(
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("语言过滤(可选)", Required = false)] string? language = null,
        [McpToolParameter("可见性过滤(public/private/internal,可选)", Required = false)] string? visibility = null,
        [McpToolParameter("只显示非 fork 仓库(可选)", Required = false)] bool? source = null,
        [McpToolParameter("只显示 fork 仓库(可选)", Required = false)] bool? fork = null,
        [McpToolParameter("只显示已归档仓库(可选)", Required = false)] bool? archived = null,
        [McpToolParameter("按 topic 过滤(可选,逗号分隔)", Required = false)] string? topic = null,
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 name,full_name,language)", Required = false)] string? json_fields = null,
        [McpToolParameter("输出档位(0=gh风格[默认] 1=精简JSON 2=完整JSON)", Required = false)] int? verbosity = null,
        [McpToolParameter("仓库名(可选,被忽略,gh repo list 列自己的仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();

        var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
        if (!string.IsNullOrWhiteSpace(language)) query["language"] = language;
        if (!string.IsNullOrWhiteSpace(visibility)) query["visibility"] = visibility;
        if (archived is not null) query["archived"] = archived.Value ? "true" : "false";
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user/repos", query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        if (verbosity == 2) return Ok(result.Body);
        return Ok(SummarizeRepoList(result.Body, source, fork, topic, json_fields));
    }

    /// <summary>
    /// 精简仓库列表 JSON — 只保留关键字段，去掉冗余 URL，便于人类浏览和 AI 解析；支持 source/fork/topic 客户端过滤
    /// </summary>
    private static readonly string[] DefaultRepoFields = ["name", "full_name", "private", "fork", "description", "language", "stargazers_count", "updated_at", "default_branch"];

    private static string SummarizeRepoList(string json, bool? source = null, bool? fork = null, string? topic = null, string? jsonFields = null) {
        var topicSet = topic?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fields = string.IsNullOrWhiteSpace(jsonFields) ? DefaultRepoFields : jsonFields.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) {
                writer.WriteStartArray();
                foreach (var repo in doc.RootElement.EnumerateArray()) {
                    var isFork = repo.TryGetProperty(GitHubJsonFields.Fork, out var f) && f.GetBoolean();
                    if (source == true && isFork) continue;
                    if (fork == true && !isFork) continue;
                    if (topicSet is { Count: > 0 } && repo.TryGetProperty("topics", out var topicsEl) && topicsEl.ValueKind == JsonValueKind.Array) {
                        var hasAnyTopic = false;
                        foreach (var t in topicsEl.EnumerateArray()) {
                            if (t.ValueKind == JsonValueKind.String && topicSet.Contains(t.GetString()!)) { hasAnyTopic = true; break; }
                        }
                        if (!hasAnyTopic) continue;
                    }
                    writer.WriteStartObject();
                    foreach (var field in fields) {
                        CopyProperty(repo, writer, field);
                    }
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
    /// 编辑仓库 — 修改描述/主页/可见性/默认分支/issues/wiki/projects/discussions/merge-methods/security/topics/template 等，调 REST API PATCH
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoEdit, "编辑仓库(description/homepage/visibility/default_branch/issues/wiki/projects/discussions/merge_methods/security/topics/template)", "github")]
    public async Task<ToolResult> GhRepoEditAsync(
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("描述(可选)", Required = false)] string? description = null,
        [McpToolParameter("主页 URL(可选)", Required = false)] string? homepage = null,
        [McpToolParameter("可见性(public/private/internal,可选)", Required = false)] string? visibility = null,
        [McpToolParameter("默认分支(可选)", Required = false)] string? default_branch = null,
        [McpToolParameter("是否启用 Issues(可选)", Required = false)] bool? has_issues = null,
        [McpToolParameter("是否启用 Wiki(可选)", Required = false)] bool? has_wiki = null,
        [McpToolParameter("是否启用 Projects(可选)", Required = false)] bool? has_projects = null,
        [McpToolParameter("合并后是否删除分支(可选)", Required = false)] bool? delete_branch_on_merge = null,
        [McpToolParameter("是否启用 Discussions(可选)", Required = false)] bool? enable_discussions = null,
        [McpToolParameter("是否允许 squash merge(可选)", Required = false)] bool? enable_squash_merge = null,
        [McpToolParameter("是否允许 merge commit(可选)", Required = false)] bool? enable_merge_commit = null,
        [McpToolParameter("是否允许 rebase merge(可选)", Required = false)] bool? enable_rebase_merge = null,
        [McpToolParameter("是否允许 auto-merge(可选)", Required = false)] bool? enable_auto_merge = null,
        [McpToolParameter("是否允许 update branch(可选)", Required = false)] bool? allow_update_branch = null,
        [McpToolParameter("是否允许 fork(可选)", Required = false)] bool? allow_forking = null,
        [McpToolParameter("是否为模板仓库(可选)", Required = false)] bool? template = null,
        [McpToolParameter("squash merge commit 消息/PR 标题模板(default/pr-title/pr-title-commits/pr-title-description,可选)", Required = false)] string? squash_merge_commit_message = null,
        [McpToolParameter("启用高级安全(可选)", Required = false)] bool? enable_advanced_security = null,
        [McpToolParameter("启用密钥扫描(可选)", Required = false)] bool? enable_secret_scanning = null,
        [McpToolParameter("启用密钥扫描推送保护(可选)", Required = false)] bool? enable_secret_scanning_push_protection = null,
        [McpToolParameter("添加 topic(可选,多个用逗号)", Required = false)] string? add_topic = null,
        [McpToolParameter("移除 topic(可选,多个用逗号)", Required = false)] string? remove_topic = null,
        [McpToolParameter("接受可见性变更后果(可选,确认标志)", Required = false)] bool? accept_visibility_change_consequences = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (!string.IsNullOrWhiteSpace(visibility) && accept_visibility_change_consequences != true)
                _logger?.LogDebug("visibility 变更未带 --accept_visibility_change_consequences,继续执行");
            SecurityAndAnalysis? security = null;
            if (enable_advanced_security is not null || enable_secret_scanning is not null || enable_secret_scanning_push_protection is not null) {
                security = new SecurityAndAnalysis {
                    AdvancedSecurity = enable_advanced_security is not null ? new SecurityFeature { Status = enable_advanced_security == true ? "enabled" : "disabled" } : null,
                    SecretScanning = enable_secret_scanning is not null ? new SecurityFeature { Status = enable_secret_scanning == true ? "enabled" : "disabled" } : null,
                    SecretScanningPushProtection = enable_secret_scanning_push_protection is not null ? new SecurityFeature { Status = enable_secret_scanning_push_protection == true ? "enabled" : "disabled" } : null,
                };
            }
            var jsonBody = JsonSerializer.Serialize(new RepoEditRequest {
                Description = description,
                Homepage = homepage,
                Visibility = visibility,
                DefaultBranch = default_branch,
                HasIssues = has_issues,
                HasWiki = has_wiki,
                HasProjects = has_projects,
                DeleteBranchOnMerge = delete_branch_on_merge,
                HasDiscussions = enable_discussions,
                AllowSquashMerge = enable_squash_merge,
                AllowMergeCommit = enable_merge_commit,
                AllowRebaseMerge = enable_rebase_merge,
                AllowAutoMerge = enable_auto_merge,
                AllowUpdateBranch = allow_update_branch,
                AllowForking = allow_forking,
                IsTemplate = template,
                SquashPrCommitMessage = squash_merge_commit_message,
                SecurityAndAnalysis = security,
            }, GitHubApiJsonContext.Safe.RepoEditRequest);
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            var topicMsg = await TryUpdateTopicsAsync(client, owner, repoName, add_topic, remove_topic, cancellationToken).ConfigureAwait(false);
            return OkBrief(result.Body, $"已编辑仓库 {owner}/{repoName}{topicMsg}");
        }).ConfigureAwait(false);

    /// <summary>
    /// 添加/移除仓库 topic — GET 当前 topics → 增删 → PUT 更新
    /// </summary>
    private async Task<string> TryUpdateTopicsAsync(IGitHubApiClient client, string owner, string repo, string? addTopic, string? removeTopic, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(addTopic) && string.IsNullOrWhiteSpace(removeTopic)) return "";
        var getResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/topics", ct: ct).ConfigureAwait(false);
        if (!getResult.Success) return "(topic 更新失败: 无法获取当前 topics)";
        List<string> currentTopics;
        try {
            using var doc = JsonDocument.Parse(getResult.Body);
            currentTopics = [];
            if (doc.RootElement.TryGetProperty("names", out var names)) {
                foreach (var n in names.EnumerateArray())
                    currentTopics.Add(n.GetString() ?? "");
            }
        } catch (Exception ex) { _logger?.LogDebug(ex, "解析 topics 失败"); return "(topic 更新失败: 解析错误)"; }
        var toAdd = ParseCsvToList(addTopic);
        var toRemove = ParseCsvToList(removeTopic);
        var updatedTopics = currentTopics.Except(toRemove, StringComparer.OrdinalIgnoreCase).Union(toAdd, StringComparer.OrdinalIgnoreCase).ToList();
        var topicsBody = JsonSerializer.Serialize(new TopicsRequest { Names = updatedTopics }, GitHubApiJsonContext.Safe.TopicsRequest);
        var putResult = await client.SendAsync(HttpMethod.Put, $"repos/{owner}/{repo}/topics", topicsBody, ct: ct).ConfigureAwait(false);
        return putResult.Success ? $"\ntopics 已更新: {string.Join(", ", updatedTopics)}" : "(topic 更新失败)";
    }

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
            var jsonBody = JsonSerializer.Serialize(new RepoArchiveRequest { Archived = true }, GitHubApiJsonContext.Safe.RepoArchiveRequest);
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}", jsonBody, ct: cancellationToken).ConfigureAwait(false);
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
            var jsonBody = JsonSerializer.Serialize(new RepoArchiveRequest { Archived = false }, GitHubApiJsonContext.Safe.RepoArchiveRequest);
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}", jsonBody, ct: cancellationToken).ConfigureAwait(false);
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
            var jsonBody = JsonSerializer.Serialize(new RepoRenameRequest { NewName = new_name }, GitHubApiJsonContext.Safe.RepoRenameRequest);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/rename", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已重命名仓库 {owner}/{repoName} → {owner}/{new_name}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 同步 Fork 仓库 — 调 REST API POST /repos/{owner}/{repo}/merge-upstream
    /// <para>source 参数仅用于兼容系统 gh CLI --source，jcc 始终同步 fork 的 parent 上游（GitHub API 限制）</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhRepoSync, "同步 Fork 仓库(从上游拉取更新)", "github")]
    public async Task<ToolResult> GhRepoSyncAsync(
        [McpToolParameter("要同步的分支(可选,默认默认分支)", Required = false)] string? branch = null,
        [McpToolParameter("上游仓库(可选,仅兼容 gh CLI --source,始终同步 fork parent)", Required = false)] string? source = null,
        [McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var defaultBranch = branch ?? "main";
            var jsonBody = JsonSerializer.Serialize(new RepoSyncRequest { Branch = defaultBranch }, GitHubApiJsonContext.Safe.RepoSyncRequest);
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
            var jsonBody = JsonSerializer.Serialize(new RepoSetDefaultRequest { DefaultBranch = branch }, GitHubApiJsonContext.Safe.RepoSetDefaultRequest);
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
            var jsonBody = JsonSerializer.Serialize(new AutolinkCreateRequest { KeyPrefix = key_prefix, UrlTemplate = url_template }, GitHubApiJsonContext.Safe.AutolinkCreateRequest);
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
                var id = al.TryGetProperty(GitHubJsonFields.Id, out var i) ? i.GetInt32() : 0;
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
            var jsonBody = JsonSerializer.Serialize(new DeployKeyAddRequest { Title = title, Key = key, ReadOnly = read_only }, GitHubApiJsonContext.Safe.DeployKeyAddRequest);
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
                var id = k.TryGetProperty(GitHubJsonFields.Id, out var i) ? i.GetInt32() : 0;
                var title = k.TryGetProperty(GitHubJsonFields.Title, out var t) ? t.GetString() ?? "" : "";
                var ro = k.TryGetProperty("read_only", out var r) && r.GetBoolean();
                var created = k.TryGetProperty(GitHubJsonFields.CreatedAt, out var c) ? c.GetString() ?? "" : "";
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
                var name = lic.TryGetProperty(GitHubJsonFields.Name, out var n) ? n.GetString() ?? "" : "";
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