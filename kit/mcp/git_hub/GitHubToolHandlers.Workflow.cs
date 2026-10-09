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
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter(WellKnownParam.JsonFields)] string? json_fields = null,
        [McpToolParameter(WellKnownParam.Verbosity)] int? verbosity = null,
        [McpToolParameter(WellKnownParam.Repo)] string? repo = null,
        [McpToolParameter(WellKnownParam.WorkingDir)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 50).ToString() };
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/workflows", query: query, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(FormatGhOutput(result.Body, verbosity, json_fields, body => SummarizeWorkflowList(body, all == true), "id,name,state,path"));
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简 Workflow 列表 — 表格格式(id, name, state, path)
    /// </summary>
    private static string SummarizeWorkflowList(string json, bool includeDisabled) {
        try {
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.WorkflowListResponse);
            if (resp is null) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine("ID\t名称\t状态\t路径");
            foreach (var wf in resp.Workflows) {
                if (!includeDisabled && wf.State == "disabled_manually") continue;
                sb.AppendLine($"{wf.Id}\t{wf.Name}\t{wf.State}\t{wf.Path}");
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
        [McpToolParameter(WellKnownParam.JsonFields)] string? json_fields = null,
        [McpToolParameter(WellKnownParam.Verbosity)] int? verbosity = null,
        [McpToolParameter(WellKnownParam.Repo)] string? repo = null,
        [McpToolParameter(WellKnownParam.WorkingDir)] string? working_dir = null,
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
            return Ok(FormatGhOutput(result.Body, verbosity, json_fields, SummarizeWorkflowView, "id,name,state,path,html_url"));
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简 Workflow 详情 — 人类可读文本
    /// </summary>
    private static string SummarizeWorkflowView(string json) {
        try {
            var wf = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.WorkflowResponse);
            if (wf is null) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine($"Workflow: {wf.Name}");
            sb.AppendLine($"ID: {wf.Id}  State: {wf.State}  Path: {wf.Path}");
            return sb.ToString().TrimEnd();
        } catch {
            return json;
        }
    }

    /// <summary>
    /// 从 workflow JSON 提取 definition(yaml 内容) — 失败返回 null
    /// </summary>
    private static string? TryExtractWorkflowYaml(string json) {
        try {
            var wf = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.WorkflowResponse);
            return wf?.Definition;
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
        [McpToolParameter(WellKnownParam.Repo)] string? repo = null,
        [McpToolParameter(WellKnownParam.WorkingDir)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var refVal = string.IsNullOrWhiteSpace(@ref) ? "main" : @ref;
            var inputsDict = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(inputs)) {
                try {
                    var parsed = JsonSerializer.Deserialize(inputs, GitHubApiJsonContext.Safe.DictionaryStringString);
                    if (parsed is not null) inputsDict = parsed;
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
        [McpToolParameter(WellKnownParam.Repo)] string? repo = null,
        [McpToolParameter(WellKnownParam.WorkingDir)] string? working_dir = null,
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
        [McpToolParameter(WellKnownParam.Repo)] string? repo = null,
        [McpToolParameter(WellKnownParam.WorkingDir)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Put, $"repos/{owner}/{repoName}/actions/workflows/{Uri.EscapeDataString(workflow_id)}/disable", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok($"已禁用 workflow {workflow_id}") : Fail(result.Error);
        }).ConfigureAwait(false);
}
