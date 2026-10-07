namespace McpToolDispatch;

/// <summary>
/// GitHub Workflow 工具 — 直调 GitHub Actions API 管理 workflow
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 列出仓库 Workflow — 调 REST API GET /actions/workflows，--all 包含已禁用
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhWorkflowList, "列出仓库 Workflow", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhWorkflowListAsync(
        [McpToolParameter("all=true 包含已禁用 workflow(默认 false)", Required = false)] bool? all = null,
        [McpToolParameter("数量限制(默认 50)", Required = false)] int? limit = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 50).ToString() };
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/workflows", query: query, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(SummarizeWorkflowList(result.Body, all == true));
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简 Workflow 列表 — 表格格式(id, name, state, path)
    /// </summary>
    private static string SummarizeWorkflowList(string json, bool includeDisabled) {
        try {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder(256);
            sb.AppendLine("ID\t名称\t状态\t路径");
            if (doc.RootElement.TryGetProperty("workflows", out var workflows) && workflows.ValueKind == JsonValueKind.Array) {
                foreach (var wf in workflows.EnumerateArray()) {
                    var id = wf.TryGetProperty("id", out var i) ? i.GetInt64() : 0;
                    var name = wf.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var state = wf.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
                    var path = wf.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    if (!includeDisabled && state == "disabled_manually") continue;
                    sb.AppendLine($"{id}\t{name}\t{state}\t{path}");
                }
            }
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>
    /// 查看 Workflow 详情 — 调 REST API GET /actions/workflows/{id}，--yaml 返回 yaml 内容
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhWorkflowView, "查看 Workflow 详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhWorkflowViewAsync(
        [McpToolParameter("Workflow ID 或名称或文件名", Required = true)] string workflow_id,
        [McpToolParameter("ref=true 返回指定分支版本(可选)", Required = false)] string? @ref = null,
        [McpToolParameter("yaml=true 返回 workflow yaml 内容", Required = false)] bool? yaml = null,
        [McpToolParameter("web=true 只返回浏览器 URL", Required = false)] bool? web = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var wfPath = $"repos/{owner}/{repoName}/actions/workflows/{Uri.EscapeDataString(workflow_id)}";
            var query = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(@ref)) query["ref"] = @ref;
            var result = await client.SendAsync(HttpMethod.Get, wfPath, query: query.Count > 0 ? query : null, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            if (web == true) {
                var url = TryExtractJsonField(result.Body, "html_url");
                return url is not null ? Ok(url) : Fail("无法解析 workflow html_url");
            }
            if (yaml == true) {
                var yamlContent = TryExtractWorkflowYaml(result.Body);
                if (yamlContent is not null) return Ok(yamlContent);
            }
            return Ok(result.Body);
        }).ConfigureAwait(false);

    /// <summary>
    /// 从 workflow JSON 提取 definition(yaml 内容) — 失败返回 null
    /// </summary>
    private static string? TryExtractWorkflowYaml(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("definition", out var def) ? def.GetString() : null;
        } catch { return null; }
    }

    /// <summary>
    /// 触发 Workflow 运行 — 调 REST API POST /actions/workflows/{id}/dispatches
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhWorkflowRun, "触发 Workflow 运行(workflow_dispatch)", "github")]
    public async Task<ToolResult> GhWorkflowRunAsync(
        [McpToolParameter("Workflow ID 或名称", Required = true)] string workflow_id,
        [McpToolParameter("运行分支或 tag(默认仓库默认分支)", Required = false)] string? @ref = null,
        [McpToolParameter("输入参数 JSON(可选,如 {\"key\":\"value\"})", Required = false)] string? inputs = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var refVal = string.IsNullOrWhiteSpace(@ref) ? "main" : @ref;
            var inputsDict = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(inputs)) {
                try {
                    using var doc = JsonDocument.Parse(inputs);
                    foreach (var prop in doc.RootElement.EnumerateObject()) inputsDict[prop.Name] = prop.Value.GetString() ?? "";
                } catch { return Fail($"inputs JSON 解析失败: {inputs}"); }
            }
            var request = new WorkflowDispatchRequest { Ref = refVal, Inputs = inputsDict };
            var body = JsonSerializer.Serialize(request, GitHubApiJsonContext.Safe.WorkflowDispatchRequest);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/actions/workflows/{Uri.EscapeDataString(workflow_id)}/dispatches", body, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok($"已触发 workflow {workflow_id} 运行(ref={refVal})") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 启用 Workflow — 调 REST API PUT /actions/workflows/{id}/enable
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhWorkflowEnable, "启用 Workflow", "github")]
    public async Task<ToolResult> GhWorkflowEnableAsync(
        [McpToolParameter("Workflow ID 或名称", Required = true)] string workflow_id,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Put, $"repos/{owner}/{repoName}/actions/workflows/{Uri.EscapeDataString(workflow_id)}/enable", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok($"已启用 workflow {workflow_id}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 禁用 Workflow — 调 REST API PUT /actions/workflows/{id}/disable
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhWorkflowDisable, "禁用 Workflow", "github")]
    public async Task<ToolResult> GhWorkflowDisableAsync(
        [McpToolParameter("Workflow ID 或名称", Required = true)] string workflow_id,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Put, $"repos/{owner}/{repoName}/actions/workflows/{Uri.EscapeDataString(workflow_id)}/disable", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok($"已禁用 workflow {workflow_id}") : Fail(result.Error);
        }).ConfigureAwait(false);
}
