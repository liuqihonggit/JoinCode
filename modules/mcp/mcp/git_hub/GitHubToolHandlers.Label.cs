namespace McpToolDispatch;

/// <summary>
/// GitHub Label 工具 — 直调 GitHub REST API 管理仓库标签
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 列出仓库标签 — 调 REST API GET /labels，支持 search/sort/order/limit
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhLabelList, "列出仓库标签(支持 search/sort/order)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhLabelListAsync(
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter("搜索标签名和描述(可选)", Required = false)] string? search = null,
        [McpToolParameter("排序(created/name,默认 created)", Required = false)] string? sort = null,
        [McpToolParameter("顺序(asc/desc,默认 asc)", Required = false)] string? order = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(common?.Repo, common?.WorkingDir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
            if (!string.IsNullOrWhiteSpace(sort)) query["sort"] = sort;
            if (!string.IsNullOrWhiteSpace(order)) query["order"] = order;
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/labels", query: query, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(FormatGhOutput(result.Body, common?.Verbosity, common?.JsonFields, body => SummarizeLabelList(body, search), "id,name,color,description"));
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简标签列表 — 表格格式(name, color, description)，可选客户端 search 过滤
    /// </summary>
    private static string SummarizeLabelList(string json, string? search) {
        try {
            var labels = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.ListLabelResponse);
            if (labels is null) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine("名称\t颜色\t描述");
            foreach (var label in labels) {
                if (!string.IsNullOrWhiteSpace(search) && !label.Name.Contains(search, StringComparison.OrdinalIgnoreCase) && !(label.Description ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                sb.AppendLine($"{label.Name}\t#{label.Color}\t{label.Description}");
            }
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>
    /// 创建标签 — 调 REST API POST /labels，--force 更新已存在标签
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhLabelCreate, "创建标签(--force 更新已存在)", "github")]
    public async Task<ToolResult> GhLabelCreateAsync(
        [McpToolParameter("标签名", Required = true)] string name,
        [McpToolParameter("颜色(6 字符 hex,如 ff0000,可选)", Required = false)] string? color = null,
        [McpToolParameter("描述(可选)", Required = false)] string? description = null,
        [McpToolParameter("force=true 已存在则更新(默认 false)", Required = false)] bool? force = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(common?.Repo, common?.WorkingDir, cancellationToken, async (client, owner, repoName) => {
            var request = new LabelCreateRequest { Name = name, Color = color, Description = description };
            var jsonBody = JsonSerializer.Serialize(request, GitHubApiJsonContext.Safe.LabelCreateRequest);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/labels", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            if (result.Success) return OkBrief(result.Body, $"已创建标签 {name}");
            if (force == true && result.StatusCode == 422) {
                var patchResult = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/labels/{Uri.EscapeDataString(name)}", jsonBody, ct: cancellationToken).ConfigureAwait(false);
                return patchResult.Success ? OkBrief(patchResult.Body, $"已更新标签 {name}") : Fail(patchResult.Error);
            }
            return Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 删除标签 — 调 REST API DELETE /labels/{name}，需 yes 确认
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhLabelDelete, "删除标签(需 yes 确认)", "github")]
    public async Task<ToolResult> GhLabelDeleteAsync(
        [McpToolParameter("标签名", Required = true)] string name,
        [McpToolParameter("是否跳过确认(默认 false)", Required = false)] bool? yes = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(common?.Repo, common?.WorkingDir, cancellationToken, async (client, owner, repoName) => {
            if (yes != true) return Fail("删除标签需要 yes=true 确认");
            var result = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/labels/{Uri.EscapeDataString(name)}", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已删除标签 {name}") : Fail(result.Error);
        }).ConfigureAwait(false);
}
