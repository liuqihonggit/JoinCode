namespace McpToolDispatch;

/// <summary>
/// GitHub API 通用调用工具 — 直调 GitHub REST API（ADR 0072）
/// <para>替代原 gh api 子命令包装，不再起 gh 子进程</para>
/// <para>避坑1: 禁用 --jq(已无需，REST 直返 JSON)</para>
/// <para>避坑3: 优先用专用工具(gh_pr_view 等),此工具用于无专用工具的 API 调用</para>
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhApi, "通用 GitHub REST API 调用(直调 api.github.com,输出完整 JSON)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhApiAsync(
        [McpToolParameter("API 路径(如 repos/owner/repo/issues)", Required = true)] string path,
        [McpToolParameter("HTTP 方法(GET/POST/PATCH/PUT/DELETE,默认 GET)", Required = false)] string? method = null,
        [McpToolParameter("请求体 JSON(可选,POST/PATCH/PUT 用)", Required = false)] string? body = null,
        [McpToolParameter("查询参数(可选,格式 key=value,多个用逗号分隔)", Required = false)] string? fields = null,
        [McpToolParameter("是否分页(默认 false,结果多时启用)", Required = false)] bool? paginate = null,
        [McpToolParameter("最大输出行数(默认 500,超出截断)", Required = false)] int? max_lines = null,
        [McpToolParameter("工作目录(可选,REST API 直调时忽略,保留兼容性)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null)
        {
            return ToolResultBuilder.Error().WithText("GitHub REST API 客户端未配置（IGitHubApiClient 未注入，请检查 DI 注册）").Build();
        }

        var httpMethod = string.IsNullOrWhiteSpace(method) ? HttpMethod.Get : new HttpMethod(method.ToUpperInvariant());
        var query = ParseFieldsToQuery(fields);
        var result = await _apiClient.SendAsync(httpMethod, path, body, query, paginate == true, cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);

        var maxLines = max_lines ?? 500;
        var truncated = TruncateLines(result.Body, maxLines);
        return Ok(truncated);
    }

    /// <summary>
    /// 解析 fields 字符串(key=value,逗号分隔)为查询参数字典
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ParseFieldsToQuery(string? fields)
    {
        if (string.IsNullOrWhiteSpace(fields)) return null;
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var pairs = fields.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx > 0 && eqIdx < pair.Length - 1)
            {
                var key = pair[..eqIdx].Trim();
                var value = pair[(eqIdx + 1)..].Trim();
                dict[key] = value;
            }
        }
        return dict.Count == 0 ? null : dict;
    }
}
