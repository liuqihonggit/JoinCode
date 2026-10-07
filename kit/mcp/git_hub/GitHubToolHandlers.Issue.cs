namespace McpToolDispatch;

/// <summary>
/// GitHub Issue 工具 — 直调 GitHub REST API（ADR 0073），替代原 gh issue 子命令包装
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 列出 Issue — 支持状态/标签/指派人/作者/提及/里程碑/搜索/类型过滤，表格格式输出
    /// <para>简单过滤(state/label/assignee/author/mention/milestone)走 issues API；复杂过滤(search/type=pr)走 search API</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueList, "列出 Issue(支持状态/标签/指派人/作者/提及/里程碑/搜索/类型过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhIssueListAsync(
        [McpToolParameter("状态(open/closed/all,默认 open)", Required = false)] string? state = null,
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("标签过滤(可选,多个用逗号)", Required = false)] string? label = null,
        [McpToolParameter("指派人过滤(可选)", Required = false)] string? assignee = null,
        [McpToolParameter("作者过滤(可选)", Required = false)] string? author = null,
        [McpToolParameter("提及过滤(可选)", Required = false)] string? mention = null,
        [McpToolParameter("里程碑过滤(可选,数字 ID 或 * 或 none)", Required = false)] string? milestone = null,
        [McpToolParameter("搜索查询(可选,GitHub search 语法)", Required = false)] string? search = null,
        [McpToolParameter("类型(issue/pr,默认 issue)", Required = false)] string? type = null,
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 number,title,url)", Required = false)] string? json_fields = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var pageCount = (limit ?? 30).ToString();
            var stateVal = string.IsNullOrWhiteSpace(state) ? "open" : state;
            var needSearch = !string.IsNullOrWhiteSpace(search) || string.Equals(type, "pr", StringComparison.OrdinalIgnoreCase);
            if (needSearch) {
                var q = BuildIssueSearchQuery(owner, repoName, stateVal, author, mention, milestone, label, assignee, search, type);
                var query = new Dictionary<string, string> { ["q"] = q, ["per_page"] = pageCount };
                var result = await client.SendAsync(HttpMethod.Get, "search/issues", query: query, ct: cancellationToken).ConfigureAwait(false);
                if (!result.Success) return Fail(result.Error);
                return Ok(!string.IsNullOrEmpty(json_fields) ? FilterJsonFields(result.Body, json_fields) : SummarizeIssueList(result.Body));
            }
            var issuesQuery = new Dictionary<string, string> { ["state"] = stateVal, ["per_page"] = pageCount };
            if (!string.IsNullOrWhiteSpace(label)) issuesQuery["labels"] = label;
            if (!string.IsNullOrWhiteSpace(assignee)) issuesQuery["assignee"] = assignee;
            if (!string.IsNullOrWhiteSpace(author)) issuesQuery["creator"] = author;
            if (!string.IsNullOrWhiteSpace(mention)) issuesQuery["mentioned"] = mention;
            if (!string.IsNullOrWhiteSpace(milestone)) issuesQuery["milestone"] = milestone;
            var issuesResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/issues", query: issuesQuery, ct: cancellationToken).ConfigureAwait(false);
            if (!issuesResult.Success) return Fail(issuesResult.Error);
            return Ok(!string.IsNullOrEmpty(json_fields) ? FilterJsonFields(issuesResult.Body, json_fields) : SummarizeIssueList(issuesResult.Body));
        }).ConfigureAwait(false);

    /// <summary>
    /// 构建 Issue search API 查询字符串 — is:{type} repo:{owner}/{repo} + 各过滤条件
    /// </summary>
    private static string BuildIssueSearchQuery(string owner, string repo, string state, string? author, string? mention, string? milestone, string? label, string? assignee, string? search, string? type) {
        var typeVal = string.Equals(type, "pr", StringComparison.OrdinalIgnoreCase) ? "pr" : "issue";
        var parts = new List<string> { $"is:{typeVal}", $"repo:{owner}/{repo}", $"state:{state}" };
        if (!string.IsNullOrWhiteSpace(author)) parts.Add($"author:{author}");
        if (!string.IsNullOrWhiteSpace(mention)) parts.Add($"mentions:{mention}");
        if (!string.IsNullOrWhiteSpace(milestone)) parts.Add($"milestone:{milestone}");
        if (!string.IsNullOrWhiteSpace(label)) foreach (var l in label.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) parts.Add($"label:{l}");
        if (!string.IsNullOrWhiteSpace(assignee)) parts.Add($"assignee:{assignee}");
        if (!string.IsNullOrWhiteSpace(search)) parts.Add(search);
        return string.Join(" ", parts);
    }

    /// <summary>
    /// 查看 Issue 详情 — 调 REST API 获取 Issue 信息，verbose=true 返回完整 JSON（从缓存读），默认精简输出
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueView, "查看 Issue 详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhIssueViewAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选,默认当前目录)", Required = false)] string? working_dir = null,
        [McpToolParameter("verbose=true 返回完整 JSON(从缓存读,不调 API); 默认 false 精简输出(调 API 更新缓存)", Required = false)] bool? verbose = null,
        [McpToolParameter("comments=true 附带评论列表", Required = false)] bool? comments = null,
        [McpToolParameter("web=true 只返回 Issue 浏览器 URL", Required = false)] bool? web = null,
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 number,title,state)", Required = false)] string? json_fields = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            var apiPath = $"repos/{owner}/{repoName}/issues/{number}";
            if (web == true) {
                var issueResult = await client.SendAsync(HttpMethod.Get, apiPath, ct: cancellationToken).ConfigureAwait(false);
                if (!issueResult.Success) return Fail(issueResult.Error);
                var url = ExtractHtmlUrl(issueResult.Body);
                return string.IsNullOrEmpty(url) ? Fail("无法从 Issue 响应中解析 html_url") : Ok(url);
            }
            if (comments == true) {
                var issueResult = await client.SendAsync(HttpMethod.Get, apiPath, ct: cancellationToken).ConfigureAwait(false);
                if (!issueResult.Success) return Fail(issueResult.Error);
                var summary = !string.IsNullOrEmpty(json_fields) ? FilterJsonFields(issueResult.Body, json_fields) : (verbose == true ? issueResult.Body : SummarizeIssue(issueResult.Body));
                var commentsResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/issues/{number}/comments", ct: cancellationToken).ConfigureAwait(false);
                if (!commentsResult.Success) return Fail(commentsResult.Error);
                return Ok($"{summary}\n\n## 评论\n{SummarizeComments(commentsResult.Body)}");
            }
            if (!string.IsNullOrEmpty(json_fields)) {
                var issueResult = await client.SendAsync(HttpMethod.Get, apiPath, ct: cancellationToken).ConfigureAwait(false);
                if (!issueResult.Success) return Fail(issueResult.Error);
                return Ok(FilterJsonFields(issueResult.Body, json_fields));
            }
            var cacheKey = BuildGhCacheKey("gh_issue_view", $"{owner}/{repoName}/{number}");
            return await GetOrFetchWithCacheAsync(client, cacheKey, apiPath, verbose, SummarizeIssue, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

    /// <summary>
    /// 创建 Issue — 支持标签/指派人/里程碑/attach/blocked_by/blocking/parent/type(后四项为较新功能提示)，调 REST API POST
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueCreate, "创建 Issue(支持标签/指派人/里程碑/attach/blocked_by/blocking/parent/type)", "github")]
    public async Task<ToolResult> GhIssueCreateAsync(
        [McpToolParameter("Issue 标题", Required = true)] string title,
        [McpToolParameter("Issue 内容(body)", Required = false)] string? body = null,
        [McpToolParameter("从文件读取 body(可选,覆盖 body 参数)", Required = false)] string? body_file = null,
        [McpToolParameter("标签(可选,多个用逗号)", Required = false)] string? label = null,
        [McpToolParameter("指派人(可选)", Required = false)] string? assignee = null,
        [McpToolParameter("里程碑 ID(可选)", Required = false)] int? milestone = null,
        [McpToolParameter("添加到 Project 编号(可选,GraphQL addProjectV2ItemById)", Required = false)] int? project = null,
        [McpToolParameter("附加文件(可选,多个用逗号,暂未支持,需文件上传 API)", Required = false)] string? attach = null,
        [McpToolParameter("被哪些 issue 阻塞(可选,多个用逗号,暂未支持,需 GraphQL sub-issue API)", Required = false)] string? blocked_by = null,
        [McpToolParameter("阻塞哪些 issue(可选,多个用逗号,暂未支持,需 GraphQL sub-issue API)", Required = false)] string? blocking = null,
        [McpToolParameter("父 issue 编号(epic,可选,暂未支持,需 GraphQL sub-issue API)", Required = false)] int? parent = null,
        [McpToolParameter("issue 类型(可选,如 Bug/Task,暂未支持,需 GraphQL issue types API)", Required = false)] string? type = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (!string.IsNullOrWhiteSpace(attach)) return Fail("--attach 暂未支持: 需要文件上传 API,请先创建 issue 再手动上传附件");
            if (!string.IsNullOrWhiteSpace(blocked_by)) return Fail("--blocked_by 暂未支持: 需要 GraphQL sub-issue API,请创建 issue 后手动设置 sub-issue 关系");
            if (!string.IsNullOrWhiteSpace(blocking)) return Fail("--blocking 暂未支持: 需要 GraphQL sub-issue API,请创建 issue 后手动设置 sub-issue 关系");
            if (parent is not null) return Fail("--parent 暂未支持: 需要 GraphQL sub-issue API,请创建 issue 后手动设置 epic 关系");
            if (!string.IsNullOrWhiteSpace(type)) return Fail("--type 暂未支持: 需要 GraphQL issue types API(Enterprise 功能),请创建 issue 后手动设置类型");
            var effectiveBody = body;
            if (body_file is not null) {
                if (!_fs.FileExists(body_file)) return Fail($"body_file 不存在: {body_file}");
                effectiveBody = await _fs.ReadAllTextAsync(body_file, cancellationToken).ConfigureAwait(false);
            }
            var request = new IssueCreateRequest {
                Title = title,
                Body = effectiveBody,
                Labels = ParseCsvToList(label),
                Assignees = ParseCsvToList(assignee),
                Milestone = milestone,
            };
            var jsonBody = JsonSerializer.Serialize(request, GitHubApiJsonContext.Safe.IssueCreateRequest);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            if (project is not null) {
                string? nodeId = null;
                try {
                    using var doc = JsonDocument.Parse(result.Body);
                    nodeId = doc.RootElement.TryGetProperty("node_id", out var n) ? n.GetString() : null;
                } catch (Exception ex) { _logger?.LogDebug(ex, "解析 Issue node_id 失败"); }
                if (!string.IsNullOrEmpty(nodeId)) {
                    var projectResult = await AddToProjectAsync(client, owner, nodeId!, project.Value, cancellationToken).ConfigureAwait(false);
                    if (!projectResult.Success) _logger?.LogWarning("添加 Issue 到 Project #{Project} 失败: {Error}", project, projectResult.Error);
                }
            }
            return OkBrief(result.Body, "Issue 创建成功");
        }).ConfigureAwait(false);

    /// <summary>
    /// 关闭 Issue — 可选附评论/关闭原因/重复标记，调 REST API PATCH state=closed
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueClose, "关闭 Issue(可附评论/原因/重复标记)", "github")]
    public async Task<ToolResult> GhIssueCloseAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("关闭评论(可选)", Required = false)] string? comment = null,
        [McpToolParameter("关闭原因(completed/not_planned,可选)", Required = false)] string? reason = null,
        [McpToolParameter("重复的 Issue 编号(可选,设置后 reason 自动 not_planned)", Required = false)] int? duplicate_of = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            var commentText = comment;
            if (duplicate_of is not null) commentText = string.IsNullOrWhiteSpace(commentText) ? $"Duplicate of #{duplicate_of}" : $"{commentText}\n\nDuplicate of #{duplicate_of}";
            if (!string.IsNullOrWhiteSpace(commentText)) {
                var commentBody = JsonSerializer.Serialize(new CommentRequest { Body = commentText }, GitHubApiJsonContext.Safe.CommentRequest);
                await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", commentBody, ct: cancellationToken).ConfigureAwait(false);
            }
            var stateReason = duplicate_of is not null ? "not_planned" : reason;
            var closeRequest = new IssueEditRequest { State = "closed", StateReason = stateReason };
            var body = JsonSerializer.Serialize(closeRequest, GitHubApiJsonContext.Safe.IssueEditRequest);
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/issues/{number}", body, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已关闭 Issue {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 评论 Issue — 调 REST API POST comments 端点
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueComment, "评论 Issue", "github")]
    public async Task<ToolResult> GhIssueCommentAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("评论内容", Required = true)] string body,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            var reqBody = JsonSerializer.Serialize(new CommentRequest { Body = body }, GitHubApiJsonContext.Safe.CommentRequest);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", reqBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已评论 Issue {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 重新打开 Issue — 可选附评论，调 REST API PATCH state=open
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueReopen, "重新打开 Issue(可附评论)", "github")]
    public async Task<ToolResult> GhIssueReopenAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("重开评论(可选)", Required = false)] string? comment = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            if (!string.IsNullOrWhiteSpace(comment)) {
                var commentBody = JsonSerializer.Serialize(new CommentRequest { Body = comment }, GitHubApiJsonContext.Safe.CommentRequest);
                await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/comments", commentBody, ct: cancellationToken).ConfigureAwait(false);
            }
            var body = JsonSerializer.Serialize(new IssueEditRequest { State = "open" }, GitHubApiJsonContext.Safe.IssueEditRequest);
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/issues/{number}", body, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已重开 Issue {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 编辑 Issue — 修改标题/body/标签/指派人/里程碑，调 REST API PATCH + POST/DELETE assignees
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueEdit, "编辑 Issue(title/body/label/assignee/milestone/add_assignee/remove_assignee)", "github")]
    public async Task<ToolResult> GhIssueEditAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("新标题(可选)", Required = false)] string? title = null,
        [McpToolParameter("新 body(可选)", Required = false)] string? body = null,
        [McpToolParameter("标签(可选,多个用逗号)", Required = false)] string? label = null,
        [McpToolParameter("指派人(可选,多个用逗号,替换全部)", Required = false)] string? assignee = null,
        [McpToolParameter("里程碑 ID(可选)", Required = false)] int? milestone = null,
        [McpToolParameter("添加指派人(可选,多个用逗号)", Required = false)] string? add_assignee = null,
        [McpToolParameter("移除指派人(可选,多个用逗号)", Required = false)] string? remove_assignee = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            if (title is not null || body is not null || label is not null || assignee is not null || milestone is not null) {
                var request = new IssueEditRequest {
                    Title = title,
                    Body = body,
                    Labels = ParseCsvToList(label),
                    Assignees = ParseCsvToList(assignee),
                    Milestone = milestone,
                };
                var jsonBody = JsonSerializer.Serialize(request, GitHubApiJsonContext.Safe.IssueEditRequest);
                var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/issues/{number}", jsonBody, ct: cancellationToken).ConfigureAwait(false);
                if (!result.Success) return Fail(result.Error);
            }
            if (!string.IsNullOrWhiteSpace(add_assignee)) {
                var assigneesBody = JsonSerializer.Serialize(new AssigneesRequest { Assignees = ParseCsvToList(add_assignee) }, GitHubApiJsonContext.Safe.AssigneesRequest);
                var addResult = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/issues/{number}/assignees", assigneesBody, ct: cancellationToken).ConfigureAwait(false);
                if (!addResult.Success) return Fail(addResult.Error);
            }
            if (!string.IsNullOrWhiteSpace(remove_assignee)) {
                var assigneesBody = JsonSerializer.Serialize(new AssigneesRequest { Assignees = ParseCsvToList(remove_assignee) }, GitHubApiJsonContext.Safe.AssigneesRequest);
                var removeResult = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/issues/{number}/assignees", assigneesBody, ct: cancellationToken).ConfigureAwait(false);
                if (!removeResult.Success) return Fail(removeResult.Error);
            }
            return OkBrief("", $"已编辑 Issue {number}");
        }).ConfigureAwait(false);

    /// <summary>
    /// 删除 Issue — 调 GraphQL mutation deleteIssue（REST API 不支持删除 issue）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueDelete, "删除 Issue(需 yes 确认,用 GraphQL)", "github")]
    public async Task<ToolResult> GhIssueDeleteAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("是否跳过确认(默认 false)", Required = false)] bool? yes = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (yes != true) return Fail("删除 Issue 需要 yes=true 确认（此操作不可逆）");
            var number = ParseNumberFromRef(issue_number);
            var issueResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/issues/{number}", ct: cancellationToken).ConfigureAwait(false);
            if (!issueResult.Success) return Fail(issueResult.Error);
            string? nodeId;
            try {
                using var doc = JsonDocument.Parse(issueResult.Body);
                nodeId = doc.RootElement.TryGetProperty("node_id", out var n) ? n.GetString() : null;
            } catch { nodeId = null; }
            if (string.IsNullOrEmpty(nodeId)) return Fail("无法从 Issue 响应中解析 node_id");
            var graphqlBody = "{\"query\":\"mutation{deleteIssue(input:{issueId:\\\"" + nodeId + "\\\"}){clientMutationId}}\"}";
            var result = await client.SendAsync(HttpMethod.Post, "graphql", graphqlBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已删除 Issue {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 锁定 Issue — 调 REST API PUT issues/{number}/lock，可选锁定原因
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueLock, "锁定 Issue(可选原因)", "github")]
    public async Task<ToolResult> GhIssueLockAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("锁定原因(off-topic/resolved/spam/too heated,可选)", Required = false)] string? reason = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            var body = string.IsNullOrWhiteSpace(reason) ? null : JsonSerializer.Serialize(new LockRequest { LockReason = reason }, GitHubApiJsonContext.Safe.LockRequest);
            var result = await client.SendAsync(HttpMethod.Put, $"repos/{owner}/{repoName}/issues/{number}/lock", body, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已锁定 Issue {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 解锁 Issue — 调 REST API DELETE issues/{number}/lock
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueUnlock, "解锁 Issue", "github")]
    public async Task<ToolResult> GhIssueUnlockAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            var result = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/issues/{number}/lock", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已解锁 Issue {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 查看 Issue 状态 — 显示当前仓库 open 状态的 issue 列表(按作者分组)
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueStatus, "查看 Issue 状态(当前仓库 open issue)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhIssueStatusAsync(
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> { ["state"] = "open", ["per_page"] = "30" };
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/issues", query: query, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(SummarizeIssueStatus(result.Body));
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简 Issue 状态 JSON — 按 author 分组显示 open issue(过滤 PR)
    /// </summary>
    private static string SummarizeIssueStatus(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var byAuthor = new Dictionary<string, List<(int number, string title)>>();
            foreach (var issue in doc.RootElement.EnumerateArray()) {
                if (issue.TryGetProperty("pull_request", out _)) continue;
                var number = issue.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
                var title = issue.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var author = issue.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var login) ? login.GetString() ?? "" : "";
                if (!byAuthor.TryGetValue(author, out var list)) { list = new(); byAuthor[author] = list; }
                list.Add((number, title));
            }
            var sb = new StringBuilder(512);
            foreach (var (author, issues) in byAuthor) {
                sb.AppendLine($"## {author}");
                foreach (var (number, title) in issues) sb.AppendLine($"  #{number}: {title}");
            }
            return sb.ToString();
        } catch {
            return json;
        }
    }

    /// <summary>
    /// 创建 Issue 开发分支 — 用 git checkout -b 创建分支(命名 issue-{number}-{name})
    /// <para>--list 列出链接分支(需 GraphQL，暂未实现)；--checkout 创建后切换</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueDevelop, "创建 Issue 开发分支(git checkout -b)", "github")]
    public async Task<ToolResult> GhIssueDevelopAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("分支名(可选,默认 issue-{number})", Required = false)] string? name = null,
        [McpToolParameter("基分支(可选,默认当前分支)", Required = false)] string? @base = null,
        [McpToolParameter("list=true 列出链接分支(需 GraphQL,暂未实现)", Required = false)] bool? list = null,
        [McpToolParameter("checkout=true 创建后切换(默认 true)", Required = false)] bool? checkout = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (list == true) return Fail("issue develop --list 需要 GraphQL listIssueLinkedBranches，暂未实现");
            if (_git is null) return Fail("git 命令执行器未配置(IGitCommandRunner 未注入)，issue develop 需要本地 git");
            var number = ParseNumberFromRef(issue_number);
            var branchName = string.IsNullOrWhiteSpace(name) ? $"issue-{number}" : name;
            var checkoutArg = (checkout ?? true) ? "-b" : "-B";
            var baseArg = string.IsNullOrWhiteSpace(@base) ? "" : $" {@base}";
            var result = await _git.ExecuteAsync($"checkout {checkoutArg} {branchName}{baseArg}", working_dir, cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok($"已创建分支 {branchName}", $"Issue #{number} 开发分支") : Fail($"创建分支失败: {result.Output}");
        }).ConfigureAwait(false);

    /// <summary>
    /// 固定 Issue — 调 GraphQL mutation pinIssue(REST API 不支持 pin)
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssuePin, "固定 Issue(GraphQL pinIssue)", "github")]
    public async Task<ToolResult> GhIssuePinAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            var nodeId = await GetIssueNodeIdAsync(client, owner, repoName, number, cancellationToken).ConfigureAwait(false);
            if (nodeId is null) return Fail($"无法获取 Issue {number} 的 node_id");
            var graphqlBody = "{\"query\":\"mutation{pinIssue(input:{issueId:\\\"" + nodeId + "\\\"}){issue{number}}}\"}";
            var result = await client.SendAsync(HttpMethod.Post, "graphql", graphqlBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已固定 Issue {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 取消固定 Issue — 调 GraphQL mutation unpinIssue
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueUnpin, "取消固定 Issue(GraphQL unpinIssue)", "github")]
    public async Task<ToolResult> GhIssueUnpinAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            var nodeId = await GetIssueNodeIdAsync(client, owner, repoName, number, cancellationToken).ConfigureAwait(false);
            if (nodeId is null) return Fail($"无法获取 Issue {number} 的 node_id");
            var graphqlBody = "{\"query\":\"mutation{unpinIssue(input:{issueId:\\\"" + nodeId + "\\\"}){issue{number}}}\"}";
            var result = await client.SendAsync(HttpMethod.Post, "graphql", graphqlBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已取消固定 Issue {number}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 转移 Issue 到另一个仓库 — 调 GraphQL mutation transferIssue(需目标 repo node_id)
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhIssueTransfer, "转移 Issue 到另一个仓库(GraphQL transferIssue)", "github")]
    public async Task<ToolResult> GhIssueTransferAsync(
        [McpToolParameter("Issue 编号或 URL", Required = true)] string issue_number,
        [McpToolParameter("目标仓库(owner/repo)", Required = true)] string destination_repo,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var number = ParseNumberFromRef(issue_number);
            var nodeId = await GetIssueNodeIdAsync(client, owner, repoName, number, cancellationToken).ConfigureAwait(false);
            if (nodeId is null) return Fail($"无法获取 Issue {number} 的 node_id");
            var destParts = destination_repo.Split('/', StringSplitOptions.TrimEntries);
            if (destParts.Length != 2) return Fail($"目标仓库格式错误: {destination_repo}(应为 owner/repo)");
            var destResult = await client.SendAsync(HttpMethod.Get, $"repos/{destParts[0]}/{destParts[1]}", ct: cancellationToken).ConfigureAwait(false);
            if (!destResult.Success) return Fail($"无法获取目标仓库: {destResult.Error}");
            string? destNodeId;
            try {
                using var doc = JsonDocument.Parse(destResult.Body);
                destNodeId = doc.RootElement.TryGetProperty("node_id", out var n) ? n.GetString() : null;
            } catch { destNodeId = null; }
            if (string.IsNullOrEmpty(destNodeId)) return Fail("无法从目标仓库响应中解析 node_id");
            var graphqlBody = "{\"query\":\"mutation{transferIssue(input:{issueId:\\\"" + nodeId + "\\\",repositoryId:\\\"" + destNodeId + "\\\"}){issue{number}}}\"}";
            var result = await client.SendAsync(HttpMethod.Post, "graphql", graphqlBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已转移 Issue {number} 到 {destination_repo}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 获取 Issue node_id — 调 REST API GET issues/{n} 提取 node_id（GraphQL mutation 需要）
    /// </summary>
    private async Task<string?> GetIssueNodeIdAsync(IGitHubApiClient client, string owner, string repo, string number, CancellationToken ct) {
        var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/issues/{number}", ct: ct).ConfigureAwait(false);
        if (!result.Success) return null;
        try {
            using var doc = JsonDocument.Parse(result.Body);
            return doc.RootElement.TryGetProperty("node_id", out var n) ? n.GetString() : null;
        } catch { return null; }
    }
}