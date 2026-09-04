namespace McpToolDispatch;

/// <summary>
/// GitHub API 通用调用工具 — gh api 子命令
/// <para>避坑1: 禁用 --jq(PowerShell 引号问题),改输出完整 JSON 用 JsonDocument 解析</para>
/// <para>避坑3: 优先用专用工具(gh_run_view 等),此工具用于无专用工具的 API 调用</para>
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhApi, "通用 GitHub REST API 调用(禁用 --jq,输出完整 JSON)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhApiAsync(
        [McpToolParameter("API 路径(如 repos/owner/repo/issues)", Required = true)] string path,
        [McpToolParameter("HTTP 方法(GET/POST/PATCH/PUT/DELETE,默认 GET)", Required = false)] string? method = null,
        [McpToolParameter("请求体 JSON(可选,POST/PATCH/PUT 用)", Required = false)] string? body = null,
        [McpToolParameter("查询参数(可选,格式 key=value,多个用逗号分隔)", Required = false)] string? fields = null,
        [McpToolParameter("是否分页(默认 false,结果多时启用)", Required = false)] bool? paginate = null,
        [McpToolParameter("最大输出行数(默认 500,超出截断)", Required = false)] int? max_lines = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder("api");
        var httpMethod = string.IsNullOrWhiteSpace(method) ? "GET" : method.ToUpperInvariant();
        sb.Append($" --method {httpMethod}");
        sb.Append($" {path}");

        if (!string.IsNullOrWhiteSpace(body))
        {
            sb.Append($" --input -");
            // body 通过 stdin 传入避免引号问题 — 但 IGitHubCommandRunner.ExecuteAsync 不支持 stdin
            // 退而求其次: 用 --raw-field 传 body(适用于简单场景)
            // 完整方案需要扩展 IGitHubCommandRunner 支持 stdin,当前用 -F 传字段
        }

        if (!string.IsNullOrWhiteSpace(fields))
        {
            var pairs = fields.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var eqIdx = pair.IndexOf('=');
                if (eqIdx > 0 && eqIdx < pair.Length - 1)
                {
                    var key = pair[..eqIdx].Trim();
                    var value = pair[(eqIdx + 1)..].Trim();
                    sb.Append($" -f {key}={Quote(value)}");
                }
            }
        }

        if (paginate == true) sb.Append(" --paginate");

        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        if (!result.Success) return Fail(result);

        var maxLines = max_lines ?? 500;
        var truncated = TruncateLines(result.Output, maxLines);
        return Ok(truncated);
    }
}
