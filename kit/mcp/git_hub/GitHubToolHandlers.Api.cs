namespace McpToolDispatch;

/// <summary>
/// GitHub API 通用调用工具 — 直调 GitHub REST API（ADR 0073）
/// <para>替代原 gh api 子命令包装，不再起 gh 子进程</para>
/// <para>支持 --jq: 简易 jq 子集表达式筛选（.field, [], select, {key: .field}, | 管道）</para>
/// <para>避坑3: 优先用专用工具(gh_pr_view 等),此工具用于无专用工具的 API 调用</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 通用 GitHub REST API 调用 — 直调 api.github.com，输出完整 JSON（截断到 max_lines 行）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhApi, "通用 GitHub REST API 调用(直调 api.github.com,输出完整 JSON)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhApiAsync(
        [McpToolParameter("API 路径(如 repos/owner/repo/issues)", Required = true)] string path,
        [McpToolParameter("HTTP 方法(GET/POST/PATCH/PUT/DELETE,默认 GET)", Required = false)] string? method = null,
        [McpToolParameter("请求体 JSON(可选,POST/PATCH/PUT 用)", Required = false)] string? body = null,
        [McpToolParameter("请求体 JSON 文件路径(可选,读取文件内容作为 body,彻底绕开命令行转义问题)", Required = false)] string? body_file = null,
        [McpToolParameter("查询参数(可选,格式 key=value,多个用逗号分隔)", Required = false)] string? fields = null,
        [McpToolParameter("jq 表达式(可选,筛选结果。支持 .field/.field.sub/[]/select(.f==\"v\" or .g==\"w\")/{k: .f}/| 管道)", Required = false)] string? jq = null,
        [McpToolParameter("是否分页(默认 false,结果多时启用)", Required = false)] bool? paginate = null,
        [McpToolParameter("最大输出行数(默认 500,超出截断)", Required = false)] int? max_lines = null,
        [McpToolParameter("工作目录(可选,REST API 直调时忽略,保留兼容性)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) {
            return ToolResultBuilder.Error().WithText("GitHub REST API 客户端未配置（IGitHubApiClient 未注入，请检查 DI 注册）").Build();
        }

        if (!string.IsNullOrWhiteSpace(body_file) && !_fs.FileExists(body_file)) {
            _logger?.LogWarning("body_file 指定的文件不存在: {FilePath}", body_file);
            return Fail("body_file 指定的文件不存在或无法读取");
        }

        var resolvedBody = await ResolveRequestBodyAsync(body, body_file).ConfigureAwait(false);

        var httpMethod = string.IsNullOrWhiteSpace(method) ? HttpMethod.Get : new HttpMethod(method.ToUpperInvariant());
        var query = ParseFieldsToQuery(fields);
        var result = await _apiClient.SendAsync(httpMethod, path, resolvedBody, query, paginate == true, cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(BuildApiErrorHint(path, result.StatusCode, result.Error));

        var output = string.IsNullOrWhiteSpace(jq)
            ? result.Body
            : SimpleJqEvaluator.Evaluate(result.Body, jq) ?? result.Body;
        var maxLines = max_lines ?? 500;
        var truncated = TruncateLines(output, maxLines);
        return Ok(truncated);
    }

    /// <summary>
    /// 解析请求体 — 优先 body_file,其次 body,并对 body 做宽容 JSON 修复
    /// <para>body_file 从文件读取,彻底绕开 PowerShell/Shell 命令行转义问题(推荐)</para>
    /// <para>body 直接传 JSON 字符串,可能被 Shell 转义破坏,通过 LlmJsonHelper.RepairJson 统一修复</para>
    /// <para>返回 null 表示 body_file 指定但文件不可读</para>
    /// </summary>
    private async Task<string?> ResolveRequestBodyAsync(string? body, string? body_file) {
        if (!string.IsNullOrWhiteSpace(body_file)) {
            if (!_fs.FileExists(body_file)) {
                _logger?.LogWarning("body_file 指定的文件不存在: {FilePath}", body_file);
                return null;
            }
            return await _fs.ReadAllText(body_file).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(body)) {
            return null;
        }

        var repairResult = LlmJsonHelper.RepairJson(body, _logger);
        return repairResult.Success ? repairResult.RepairedJson : body.Trim();
    }

    /// <summary>
    /// 解析 fields 字符串(key=value,逗号分隔)为查询参数字典
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ParseFieldsToQuery(string? fields) {
        if (string.IsNullOrWhiteSpace(fields)) return null;
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var span = fields.AsSpan();
        while (!span.IsEmpty) {
            var commaIdx = span.IndexOf(',');
            var pair = commaIdx < 0 ? span : span[..commaIdx];
            span = commaIdx < 0 ? default : span[(commaIdx + 1)..];
            if (pair.IsEmpty) continue;
            var eqIdx = pair.IndexOf('=');
            if (eqIdx > 0 && eqIdx < pair.Length - 1) {
                var key = pair[..eqIdx].Trim().ToString();
                var value = pair[(eqIdx + 1)..].Trim().ToString();
                dict[key] = value;
            }
        }
        return dict.Count == 0 ? null : dict;
    }

    /// <summary>
    /// 构建 API 错误诱导提示 — 根据端点模式 + 状态码给出"为什么失败 + 接下来怎么做"
    /// <para>原则(AGENTS.md 错误提示必须有诱导方式): 禁止纯拒绝无引导,否则 AI 不知道错误含义会换命令尝试</para>
    /// </summary>
    private static string BuildApiErrorHint(string path, int statusCode, string originalError) {
        var hint = (statusCode, path) switch {
            (404, var p) when p.Contains("auto-merge", StringComparison.OrdinalIgnoreCase)
                => "404 Not Found — 可能原因: ① 仓库 Settings → General → Pull Requests 未勾选 Allow auto-merge ② PR 不存在 ③ 端点路径错误。建议: 改用 gh_pr_merge --auto_merge 启用(走 GraphQL enablePullRequestAutoMerge)",
            (404, var p) when p.StartsWith("repos/", StringComparison.OrdinalIgnoreCase) && p.Count(c => c == '/') >= 2
                => $"404 Not Found — 可能原因: ① 仓库不存在或无权限 ② 端点路径错误。路径: {p}",
            (403, var p) when p.Contains("/merge", StringComparison.OrdinalIgnoreCase)
                => "403 Forbidden — 可能原因: ① 分支保护规则未满足(required checks 未通过/未匹配) ② Token 缺少 repo scope ③ 需 admin 强制合并。建议: gh pr checks 查 CI 状态, 或 gh_branch_sync_protection 同步 check 名",
            (403, _)
                => "403 Forbidden — 可能原因: ① Token 缺少所需 scope ② Rate limit 触发 ③ 资源无权限",
            (422, _)
                => "422 Unprocessable Entity — 请求体格式错误或字段值非法, 请检查 body JSON 结构",
            (401, _)
                => "401 Unauthorized — Token 无效或已过期, 请检查 JCC_GITHUB_TOKEN / GITHUB_TOKEN 环境变量",
            _ => $"{statusCode} — {originalError}"
        };
        return hint;
    }
}