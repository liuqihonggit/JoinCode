namespace McpToolDispatch;

/// <summary>
/// GitHub Gist 工具 — 直调 GitHub REST API，DTO + JsonContext 双向转换
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 列出当前用户的 Gist — 调 GET /gists，反序列化为 List&lt;GistListItem&gt;
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhGistList, "列出当前用户的 Gist", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhGistListAsync(
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 id,description,public)", Required = false)] string? json = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
        var result = await _apiClient.SendAsync(HttpMethod.Get, "gists", query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        if (!string.IsNullOrEmpty(json)) return Ok(FilterJsonFields(result.Body, json));
        var gists = JsonSerializer.Deserialize(result.Body, GitHubApiJsonContext.Safe.ListGistListItem);
        return gists is null ? Fail("解析 Gist 列表失败") : Ok(SummarizeGistList(gists));
    }

    private static string SummarizeGistList(List<GistListItem> gists) {
        var sb = new StringBuilder(256);
        sb.AppendLine("ID\t描述\t公开\t文件数");
        foreach (var g in gists) {
            var fileCount = g.Files?.Count ?? 0;
            sb.AppendLine($"{g.Id}\t{g.Description ?? ""}\t{g.Public}\t{fileCount}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 查看 Gist 详情 — 调 GET /gists/{id}，反序列化为 GistResponse
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhGistView, "查看 Gist 详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhGistViewAsync(
        [McpToolParameter("Gist ID", Required = true)] string gist_id,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, $"gists/{gist_id}", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        var gist = JsonSerializer.Deserialize(result.Body, GitHubApiJsonContext.Safe.GistResponse);
        return gist is null ? Fail("解析 Gist 详情失败") : Ok(SummarizeGistView(gist));
    }

    private static string SummarizeGistView(GistResponse gist) {
        var sb = new StringBuilder(512);
        sb.AppendLine($"描述: {gist.Description ?? ""}");
        if (gist.Files is not null) {
            foreach (var (name, file) in gist.Files) {
                sb.AppendLine($"--- {name} ---");
                sb.Append(file.Content ?? "");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 创建 Gist — 调 POST /gists，序列化 GistCreateRequest DTO
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhGistCreate, "创建 Gist", "github")]
    public async Task<ToolResult> GhGistCreateAsync(
        [McpToolParameter("文件名", Required = true)] string filename,
        [McpToolParameter("文件内容", Required = true)] string content,
        [McpToolParameter("描述(可选)", Required = false)] string? description = null,
        [McpToolParameter("public=true 公开(默认 false 私有)", Required = false)] bool? @public = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var request = new GistCreateRequest {
            Files = new() { [filename] = new GistFileContent { Content = content } },
            Description = description,
            Public = @public == true,
        };
        var body = JsonSerializer.Serialize(request, GitHubApiJsonContext.Safe.GistCreateRequest);
        var result = await _apiClient.SendAsync(HttpMethod.Post, "gists", body, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? OkBrief(result.Body, "Gist 创建成功") : Fail(result.Error);
    }

    /// <summary>
    /// 删除 Gist — 调 DELETE /gists/{id}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhGistDelete, "删除 Gist", "github")]
    public async Task<ToolResult> GhGistDeleteAsync(
        [McpToolParameter("Gist ID", Required = true)] string gist_id,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Delete, $"gists/{gist_id}", ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok($"已删除 Gist {gist_id}") : Fail(result.Error);
    }
}
