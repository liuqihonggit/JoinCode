namespace McpToolDispatch;

/// <summary>
/// GitHub API 通用调用工具 — 直调 GitHub REST API（ADR 0073）
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
        [McpToolParameter("请求体 JSON 文件路径(可选,读取文件内容作为 body,彻底绕开命令行转义问题)", Required = false)] string? body_file = null,
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

        var resolvedBody = ResolveRequestBody(body, body_file);
        if (resolvedBody is null)
        {
            return Fail("body_file 指定的文件不存在或无法读取");
        }

        var httpMethod = string.IsNullOrWhiteSpace(method) ? HttpMethod.Get : new HttpMethod(method.ToUpperInvariant());
        var query = ParseFieldsToQuery(fields);
        var result = await _apiClient.SendAsync(httpMethod, path, resolvedBody, query, paginate == true, cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);

        var maxLines = max_lines ?? 500;
        var truncated = TruncateLines(result.Body, maxLines);
        return Ok(truncated);
    }

    /// <summary>
    /// 解析请求体 — 优先 body_file,其次 body,并对 body 做宽容 JSON 修复
    /// <para>body_file 从文件读取,彻底绕开 PowerShell/Shell 命令行转义问题(推荐)</para>
    /// <para>body 直接传 JSON 字符串,可能被 Shell 转义破坏,做宽容修复</para>
    /// <para>返回 null 表示 body_file 指定但文件不可读</para>
    /// </summary>
    private string? ResolveRequestBody(string? body, string? body_file)
    {
        if (!string.IsNullOrWhiteSpace(body_file))
        {
            if (!_fs.FileExists(body_file))
            {
                _logger?.LogWarning("body_file 指定的文件不存在: {FilePath}", body_file);
                return null;
            }
            return _fs.ReadAllText(body_file);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return NormalizeJsonBody(body);
    }

    /// <summary>
    /// 宽容 JSON body 修复 — 修复 Shell 命令行转义导致的常见 JSON 破坏
    /// <para>常见问题1: PowerShell 双引号字符串中 \n 被解释为换行符 → 替换回 \n</para>
    /// <para>常见问题2: 外层多余引号 → 去除</para>
    /// <para>常见问题3: 实际换行符在 JSON 字符串值中 → 替换为 \n</para>
    /// <para>如果原始 body 已是合法 JSON,直接返回不做修改(幂等)</para>
    /// </summary>
    private static string NormalizeJsonBody(string body)
    {
        var trimmed = body.AsSpan().Trim();
        if (trimmed.Length == 0)
        {
            return body;
        }

        if (IsValidJson(trimmed))
        {
            return trimmed.ToString();
        }

        var repaired = TryRepairJson(trimmed);
        if (repaired is not null && IsValidJson(repaired.AsSpan()))
        {
            return repaired;
        }

        return trimmed.ToString();
    }

    /// <summary>
    /// 验证字符串是否为合法 JSON
    /// </summary>
    private static bool IsValidJson(ReadOnlySpan<char> json)
    {
        try
        {
            JsonDocument.Parse(json.ToString());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 尝试修复 Shell 转义破坏的 JSON — 按常见问题逐步修复
    /// <para>问题1: 外层多余引号 → 去除</para>
    /// <para>问题2: 实际换行符在 JSON 字符串值中 → 替换为 \n</para>
    /// <para>问题3: PowerShell 5.1 吃掉双引号 → {key:value} → {"key":"value"}</para>
    /// </summary>
    private static string? TryRepairJson(ReadOnlySpan<char> json)
    {
        var text = json.ToString();

        var hasOuterQuotes = text.Length >= 2
            && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\''));
        if (hasOuterQuotes)
        {
            text = text[1..^1];
        }

        if (text.Contains('\r') || text.Contains('\n'))
        {
            text = text.Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n");
        }

        if (text.Contains('\t'))
        {
            text = text.Replace("\t", "\\t");
        }

        if (IsValidJson(text.AsSpan()))
        {
            return text;
        }

        return TryRestoreStrippedQuotes(text);
    }

    /// <summary>
    /// 尝试恢复被 PowerShell 吃掉的双引号 — {key:value} → {"key":"value"}
    /// <para>PowerShell 5.1 传参给原生 exe 时会吃掉双引号,导致 JSON 不合法</para>
    /// <para>策略: 用正则给无引号的 key 和 string value 加引号,然后验证</para>
    /// </summary>
    private static string? TryRestoreStrippedQuotes(string text)
    {
        if (!text.StartsWith('{') && !text.StartsWith('['))
        {
            return null;
        }

        var repaired = QuoteUnquotedKeys(text);
        repaired = QuoteUnquotedValues(repaired);

        return IsValidJson(repaired.AsSpan()) ? repaired : null;
    }

    /// <summary>
    /// 给无引号的 JSON key 加引号 — {key: → {"key":  ,key: → ,"key":
    /// </summary>
    private static string QuoteUnquotedKeys(string text)
        => JsonKeyQuoteRegex().Replace(text, "$1\"$2\"$3");

    /// <summary>
    /// 给无引号的 JSON string value 加引号 — :value, → :"value",  :value} → :"value"}
    /// <para>跳过数字、true/false/null、嵌套对象{}和数组[]</para>
    /// </summary>
    private static string QuoteUnquotedValues(string text)
        => JsonValueQuoteRegex().Replace(text, "$1\"$2\"$3");

    [GeneratedRegex(@"([{,]\s*)([^\s:{}\[\],""]+)(\s*:)", RegexOptions.Compiled)]
    private static partial Regex JsonKeyQuoteRegex();

    [GeneratedRegex(@"(:\s*)(?!true\b|false\b|null\b)([^\s{}\[\],:""]+)(\s*[,}\]])", RegexOptions.Compiled)]
    private static partial Regex JsonValueQuoteRegex();

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
