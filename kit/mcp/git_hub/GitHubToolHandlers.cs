namespace McpToolDispatch;

/// <summary>
/// GitHub CLI 工具处理器 — 将 gh 子命令暴露为 MCP 工具供 LLM 直接调用
/// <para>避坑要点（AGENTS.md 坑1-5 内化）：</para>
/// <para>1. 禁用 --jq，改 gh --json 输出完整 JSON 后用 JsonDocument 解析（AOT 友好）</para>
/// <para>2. 大日志加 maxLines 截断（默认 200），优先 --job 精准拉单 job</para>
/// <para>3. Release 下载复用 IDownloader 多线程分片 + 断点续传</para>
/// <para>4. pr checks 正确处理 skipping 语义（非失败）</para>
/// <para>5. 不经 PowerShell 管道，C# Process 直接调 gh</para>
/// </summary>
[McpToolDispatch(ToolCategory.GitHub)]
public partial class GitHubToolHandlers {
    internal readonly IGitHubApiClient? _apiClient;
    private readonly IGitCommandRunner? _git;
    private readonly IDownloader _downloader;
    private readonly IFileSystem _fs;
    private readonly ILogger<GitHubToolHandlers>? _logger;
    private readonly GitHubRunLogFetcher _logFetcher;
    private readonly GitHubRunLogFilterRunner? _logFilterRunner;
    private readonly GitHubRunLogCache? _logCacheService;

    /// <summary>
    /// 日志过滤运行器 — 仅在 _apiClient 配置后可用(ExecuteGhAsync 已守卫)
    /// </summary>
    private GitHubRunLogFilterRunner LogFilterRunner =>
        _logFilterRunner ?? throw new InvalidOperationException("日志过滤运行器未初始化(API 客户端未配置)");

    /// <summary>
    /// 日志缓存服务 — 仅在 _apiClient 配置后可用(ExecuteGhAsync 已守卫)
    /// </summary>
    private GitHubRunLogCache LogCacheService =>
        _logCacheService ?? throw new InvalidOperationException("日志缓存服务未初始化(API 客户端未配置)");

    /// <summary>
    /// 统一持久化管道 — 异步串行写缓存文件到 .jcc/gh_cache/,不阻塞调用方
    /// <para>复用 ADR 0068 统一管道(IPersistencePipeline),替代专用 GitHubCacheWriteActor</para>
    /// </summary>
    private readonly IPersistencePipeline _pipeline;

    /// <summary>
    /// 创建 GitHubToolHandlers 实例
    /// </summary>
    /// <param name="downloader">文件下载器（Release asset 下载用）</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="pipeline">统一持久化管道（异步写缓存文件）</param>
    /// <param name="apiClient">GitHub REST API 客户端（可选，未注入时 API 工具返回未配置错误）</param>
    /// <param name="git">git 命令执行器（可选，未注入时 clone/checkout 等本地 git 工具返回错误）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="kvStore">KV 缓存存储（可选，LSM-Tree PithosKvStore，用于日志缓存避免重复下载）</param>
    public GitHubToolHandlers(
        IDownloader downloader,
        IFileSystem fs,
        IPersistencePipeline pipeline,
        IGitHubApiClient? apiClient = null,
        IGitCommandRunner? git = null,
        ILogger<GitHubToolHandlers>? logger = null,
        IKvStore? kvStore = null) {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _apiClient = apiClient;
        _git = git;
        _logger = logger;
        _logFetcher = new GitHubRunLogFetcher();
        _logFilterRunner = apiClient is not null ? new GitHubRunLogFilterRunner(apiClient, kvStore) : null;
        _logCacheService = apiClient is not null ? new GitHubRunLogCache(apiClient, fs, pipeline, logger) : null;
    }

    // === 共用辅助方法 ===

    /// <summary>
    /// 转义并引用命令行参数 — 值用双引号包裹，内部双引号转义
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string Quote(string value) {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (!value.Contains('"')) return string.Concat("\"", value, "\"");
        return string.Concat("\"", value.Replace("\"", "\\\""), "\"");
    }

    /// <summary>
    /// 截断输出到指定行数 — 避免大日志撑爆 LLM 上下文（避坑2/5）
    /// </summary>
    private static string TruncateLines(string output, int maxLines) {
        if (string.IsNullOrEmpty(output) || maxLines <= 0) return output;
        var ranges = LineSpanIndexer.BuildLineRanges(output.AsSpan());
        if (ranges.Count <= maxLines) return output;
        var sb = new StringBuilder(maxLines * 80);
        var span = output.AsSpan();
        for (var i = 0; i < maxLines; i++) {
            var (start, length) = ranges[i];
            sb.Append(span.Slice(start, length));
            sb.Append('\n');
        }
        sb.Append($"... [已截断，共 {ranges.Count} 行，仅显示前 {maxLines} 行。如需更多请缩小过滤范围或用 --job 精准拉取]");
        return sb.ToString();
    }

    /// <summary>
    /// 构建失败 ToolResult(直接错误消息)
    /// </summary>
    internal static ToolResult Fail(string message) {
        return ToolResultBuilder.Error().WithText(message).Build();
    }

    /// <summary>
    /// 构建成功 ToolResult
    /// </summary>
    internal static ToolResult Ok(string output, string? prefix = null) {
        var text = string.IsNullOrEmpty(prefix) ? output : $"{prefix}\n{output}";
        return ToolResultBuilder.Success().WithText(text).Build();
    }

    /// <summary>
    /// 统一三档输出格式化 — json_fields 优先 > verbosity=2 完整JSON > verbosity=1 精简JSON > 默认 Summarizer
    /// <para>verbosity: 0=gh风格人类可读(默认) 1=精简JSON 2=完整JSON</para>
    /// </summary>
    internal static string FormatGhOutput(string body, int? verbosity, string? json_fields, Func<string, string> summarize, string? compactFields = null) {
        if (!string.IsNullOrEmpty(json_fields)) return FilterJsonFields(body, json_fields);
        return verbosity switch {
            2 => body,
            1 => FilterJsonFields(body, compactFields ?? "id,number,title,state,name"),
            _ => summarize(body)
        };
    }

    /// <summary>
    /// 构建精简成功 ToolResult — 从 JSON body 提取 html_url，只返回确认消息 + URL（不返回整个 JSON body）
    /// <para>用于 Create/Update/Delete 操作，成功时无需返回完整响应体，只给确认 + 可点击链接</para>
    /// </summary>
    internal static ToolResult OkBrief(string jsonBody, string message) {
        var url = TryExtractJsonField(jsonBody, "html_url") ?? TryExtractJsonField(jsonBody, "url");
        return url is not null ? Ok(url, message) : Ok(message);
    }

    /// <summary>
    /// 从 JSON 字符串中提取指定字符串字段值（轻量 Span 解析，不构建 JsonDocument）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string? TryExtractJsonField(string json, string fieldName) {
        var patternLen = fieldName.Length + 4;
        var pattern = patternLen <= 128 ? stackalloc char[patternLen] : new char[patternLen].AsSpan();
        pattern[0] = '"';
        fieldName.AsSpan().CopyTo(pattern[1..]);
        pattern[1 + fieldName.Length] = '"';
        pattern[2 + fieldName.Length] = ':';
        pattern[3 + fieldName.Length] = '"';
        var span = json.AsSpan();
        var idx = span.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        idx += patternLen;
        var end = span[idx..].IndexOf('"');
        if (end < 0) return null;
        var valueStart = idx;
        var valueEnd = idx + end;
        return valueEnd > valueStart ? json[valueStart..valueEnd] : null;
    }

    /// <summary>
    /// GitHub REST API 客户端未配置错误
    /// </summary>
    internal static ToolResult ApiClientNotConfigured() =>
        ToolResultBuilder.Error().WithText("GitHub REST API 客户端未配置（IGitHubApiClient 未注入）").Build();

    /// <summary>
    /// 仓库 owner/repo 解析失败错误
    /// </summary>
    internal static ToolResult RepoNotResolved() =>
        ToolResultBuilder.Error().WithText("无法解析仓库 owner/repo（请传 repo 参数或确保当前目录是 GitHub 仓库）").Build();

    /// <summary>
    /// 从 PR/Issue 编号或 URL 提取数字编号 — 如 "123" → "123", "https://github.com/o/r/pull/123" → "123"
    /// <para>合并原 ParsePrNumber/ParseIssueNumber(逐字符相同的重复实现)</para>
    /// </summary>
    private static string ParseNumberFromRef(string numberOrUrl) {
        if (string.IsNullOrEmpty(numberOrUrl)) return numberOrUrl;
        var lastSlash = numberOrUrl.LastIndexOf('/');
        if (lastSlash < 0) return numberOrUrl;
        return numberOrUrl[(lastSlash + 1)..];
    }

    /// <summary>
    /// 逗号分隔字符串转 List — 如 "bug,feat" → ["bug","feat"]，空/空白返回空 List
    /// <para>DTO 序列化用：Labels/Assignees 等字段从 CSV 参数构建</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static List<string> ParseCsvToList(string? csv) {
        if (string.IsNullOrWhiteSpace(csv)) return new();
        return new(csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// 解析 milestone 名称到 ID — GET /repos/{owner}/{repo}/milestones 查找 title 匹配项
    /// <para>系统 gh --milestone 接受 name 而非 ID,需先查 milestones 列表解析</para>
    /// </summary>
    private async Task<int?> ResolveMilestoneIdAsync(IGitHubApiClient client, string owner, string repoName, string milestoneName, CancellationToken ct) {
        var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/milestones", query: new Dictionary<string, string> { ["state"] = "all", ["per_page"] = "100" }, ct: ct).ConfigureAwait(false);
        if (!result.Success) return null;
        try {
            using var doc = JsonDocument.Parse(result.Body);
            foreach (var m in doc.RootElement.EnumerateArray()) {
                if (m.TryGetProperty(GitHubJsonFields.Title, out var t) && t.GetString() == milestoneName) {
                    return m.TryGetProperty(GitHubJsonFields.Number, out var n) ? n.GetInt32() : null;
                }
            }
        } catch (Exception ex) { _logger?.LogWarning(ex, "解析 milestones 响应失败"); }
        return null;
    }

    /// <summary>
    /// 守卫编排模板 — client 检查 + owner/repo 解析,失败短路返回错误,成功执行 apiCall(client, owner, repo)
    /// <para>消除 21 处重复的 client 检查 + ResolveOwnerRepoAsync 样板,主方法只写 API 调用核心逻辑</para>
    /// <para>client 作为参数传入 apiCall,调用方直接用 client 而非 _apiClient!,消除空抑制</para>
    /// </summary>
    private async Task<ToolResult> ExecuteGhAsync(
        string? repo, string? workingDir, CancellationToken ct,
        Func<IGitHubApiClient, string, string, Task<ToolResult>> apiCall) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, workingDir, ct).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;
        return await apiCall(_apiClient, owner, repoName).ConfigureAwait(false);
    }

    /// <summary>
    /// verbosity 三档缓存模板 — verbosity=2 优先读缓存,未命中或 verbosity=0/1 调 API,成功后写缓存,按 verbosity 决定输出格式
    /// <para>消除 GhPrView/GhIssueView/GhRepoView 三处相同的缓存读写样板</para>
    /// <para>verbosity: 0=gh风格人类可读(默认) 1=精简JSON 2=完整JSON(从缓存读)</para>
    /// </summary>
    private async Task<ToolResult> GetOrFetchWithCacheAsync(
        IGitHubApiClient client, string cacheKey, string apiPath, int? verbosity,
        string? json_fields, Func<string, string> summarize, string? compactFields,
        CancellationToken ct) {
        if (!string.IsNullOrEmpty(json_fields)) {
            var result0 = await client.SendAsync(HttpMethod.Get, apiPath, ct: ct).ConfigureAwait(false);
            if (!result0.Success) return Fail(result0.Error);
            return Ok(FilterJsonFields(result0.Body, json_fields));
        }
        if (verbosity == 2) {
            var cached = TryGetGhCache(cacheKey);
            if (cached is not null) return Ok(cached);
        }
        var result = await client.SendAsync(HttpMethod.Get, apiPath, ct: ct).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        SaveGhCache(cacheKey, result.Body);
        return Ok(verbosity switch {
            2 => result.Body,
            1 => FilterJsonFields(result.Body, compactFields ?? "id,number,title,state,name"),
            _ => summarize(result.Body)
        });
    }

    /// <summary>
    /// JSON 字段过滤 — 从 JSON 中只提取指定字段(逗号分隔),支持数组和 search API 包装格式
    /// <para>用于 --json 参数: gh pr list --json number,title,url → [{"number":1,"title":"x","url":"y"}]</para>
    /// <para>数组: 过滤每个元素; 对象有 items 数组: 过滤 items; 对象无 items: 过滤自身</para>
    /// </summary>
    /// <param name="json">原始 JSON 字符串</param>
    /// <param name="fields">逗号分隔的字段名列表</param>
    /// <returns>只包含指定字段的 JSON 字符串;解析失败回退原始 json</returns>
    private static string FilterJsonFields(string json, string fields) {
        try {
            using var doc = JsonDocument.Parse(json);
            var fieldList = fields.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) {
                WriteFilteredElement(writer, doc.RootElement, fieldList);
                writer.Flush();
            }
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        } catch {
            return json;
        }
    }

    /// <summary>
    /// 递归写入过滤后的 JSON 元素
    /// </summary>
    private static void WriteFilteredElement(Utf8JsonWriter writer, JsonElement element, string[] fields) {
        if (element.ValueKind == JsonValueKind.Array) {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) {
                WriteFilteredElement(writer, item, fields);
            }
            writer.WriteEndArray();
        } else if (element.ValueKind == JsonValueKind.Object) {
            var arrayProp = FindArrayProperty(element);
            if (arrayProp is not null) {
                writer.WriteStartArray();
                foreach (var item in element.GetProperty(arrayProp).EnumerateArray()) {
                    WriteFilteredObject(writer, item, fields);
                }
                writer.WriteEndArray();
            } else {
                WriteFilteredObject(writer, element, fields);
            }
        } else {
            element.WriteTo(writer);
        }
    }

    /// <summary>
    /// 查找对象中的列表数组属性 — 优先 items,其次 workflows/workflow_runs/secrets/variables/releases/labels/runs 等已知包装属性
    /// </summary>
    private static readonly string[] ArrayPropertyCandidates = ["items", "workflows", "workflow_runs", "secrets", "variables", "releases", "labels", "runs", "issues", "pulls"];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string? FindArrayProperty(JsonElement element) {
        foreach (var name in ArrayPropertyCandidates) {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array) return name;
        }
        return null;
    }

    /// <summary>
    /// 写入过滤后的 JSON 对象 — 只包含指定字段
    /// </summary>
    private static void WriteFilteredObject(Utf8JsonWriter writer, JsonElement element, string[] fields) {
        writer.WriteStartObject();
        foreach (var field in fields) {
            if (element.TryGetProperty(field, out var value)) {
                writer.WritePropertyName(field);
                value.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// 精简 PR JSON 输出 — 提取关键字段构建人类可读文本
    /// </summary>
    private static string SummarizePr(string json) {
        try {
            var pr = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.PrDetailResponse);
            if (pr is null) return json;
            var sb = new StringBuilder(512);
            var mergeable = pr.Mergeable?.ToString() ?? "unknown";
            sb.AppendLine($"PR #{pr.Number}: {pr.Title}");
            sb.AppendLine($"状态: {pr.State}{(pr.Draft ? " (draft)" : "")} (mergeable: {mergeable}, mergeable_state: {pr.MergeableState ?? ""})");
            sb.AppendLine($"作者: {pr.User?.Login ?? ""}");
            sb.AppendLine($"分支: {pr.Head?.Ref ?? ""} → {pr.Base?.Ref ?? ""}");
            sb.AppendLine($"变更: +{pr.Additions} -{pr.Deletions} ({pr.ChangedFiles} files)");
            sb.Append($"URL: {pr.HtmlUrl ?? ""}");
            return sb.ToString();
        } catch {
            return json;
        }
    }

    /// <summary>
    /// 精简 Issue JSON 输出 — 提取关键字段构建人类可读文本
    /// </summary>
    private static string SummarizeIssue(string json) {
        try {
            var issue = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.IssueDetailResponse);
            if (issue is null) return json;
            var sb = new StringBuilder(512);
            var labels = string.Join(", ", issue.Labels.Select(l => l.Name));
            sb.AppendLine($"Issue #{issue.Number}: {issue.Title}");
            sb.AppendLine($"状态: {issue.State}");
            sb.AppendLine($"作者: {issue.User?.Login ?? ""}");
            if (!string.IsNullOrEmpty(labels)) sb.AppendLine($"标签: {labels}");
            sb.AppendLine($"创建: {issue.CreatedAt ?? ""}");
            sb.Append($"URL: {issue.HtmlUrl ?? ""}");
            return sb.ToString();
        } catch {
            return json;
        }
    }

    /// <summary>
    /// 精简 Repo JSON 输出 — 提取关键字段构建人类可读文本
    /// </summary>
    private static string SummarizeRepo(string json) {
        try {
            var repo = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.RepoDetailResponse);
            if (repo is null) return json;
            var sb = new StringBuilder(512);
            sb.AppendLine($"仓库: {repo.FullName}");
            sb.AppendLine($"可见性: {(repo.Private ? "private" : "public")}");
            sb.AppendLine($"默认分支: {repo.DefaultBranch ?? ""}");
            sb.AppendLine($"Stars: {repo.StargazersCount}, Forks: {repo.ForksCount}");
            sb.Append($"URL: {repo.HtmlUrl ?? ""}");
            return sb.ToString();
        } catch {
            return json;
        }
    }

    /// <summary>
    /// 精简 PR 列表 JSON — 表格格式(number, state, title, author)，兼容 pulls API 数组和 search API {items} 格式
    /// </summary>
    private static string SummarizePrList(string json) {
        try {
            List<PrListItemResponse>? prs;
            try {
                prs = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.ListPrListItemResponse);
            } catch (JsonException) {
                var wrapper = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.PrListItemsResponse);
                prs = wrapper?.Items;
            }
            if (prs is null) return json;
            var sb = new StringBuilder(512);
            sb.AppendLine("PR#\t状态\t标题\t作者");
            foreach (var pr in prs) sb.AppendLine($"{pr.Number}\t{pr.State}\t{pr.Title}\t{pr.User?.Login ?? ""}");
            return sb.ToString();
        } catch {
            return json;
        }
    }

    /// <summary>
    /// 精简 Issue 列表 JSON — 表格格式(number, state, title, author)
    /// </summary>
    private static string SummarizeIssueList(string json) {
        try {
            List<IssueListItemResponse>? issues;
            try {
                issues = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.ListIssueListItemResponse);
            } catch (JsonException) {
                var wrapper = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.IssueListItemsResponse);
                issues = wrapper?.Items;
            }
            if (issues is null) return json;
            var sb = new StringBuilder(512);
            sb.AppendLine("Issue#\t状态\t标题\t作者");
            foreach (var issue in issues) sb.AppendLine($"{issue.Number}\t{issue.State}\t{issue.Title}\t{issue.User?.Login ?? ""}");
            return sb.ToString();
        } catch {
            return json;
        }
    }

    /// <summary>
    /// 构建 GitHub 工具缓存 key — {toolName}_{SHA256(argsJson)[..8]}.json
    /// </summary>
    private static string BuildGhCacheKey(string toolName, string argsJson) {
        var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(argsJson));
        var hashHex = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
        return $"{toolName}_{hashHex}.json";
    }

    /// <summary>
    /// 尝试读取 GitHub 工具缓存 — verbose=true 时优先用缓存节约 API
    /// </summary>
    private string? TryGetGhCache(string cacheKey) {
        var cacheDir = GetCacheDir(null);
#pragma warning disable JCC9001
        var cachePath = Path.Combine(cacheDir, cacheKey);
        if (!File.Exists(cachePath))
            return null;
        try {
            return File.ReadAllText(cachePath);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to read gh cache {Key}", cacheKey);
            return null;
        }
#pragma warning restore JCC9001
    }

    /// <summary>
    /// 保存 GitHub 工具缓存 — 默认调用时更新缓存保证数据新鲜
    /// </summary>
    private void SaveGhCache(string cacheKey, string json) {
        var cacheDir = GetCacheDir(null);
#pragma warning disable JCC9001
        Directory.CreateDirectory(cacheDir);
        var cachePath = Path.Combine(cacheDir, cacheKey);
        try {
            File.WriteAllText(cachePath, json);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to save gh cache {Key}", cacheKey);
        }
#pragma warning restore JCC9001
    }

    /// <summary>
    /// 获取缓存目录路径 — {workingDir}/.jcc/gh_cache/ 或 {cwd}/.jcc/gh_cache/
    /// <para>项目级缓存,跨进程共享,24h 过期</para>
    /// </summary>
    private string GetCacheDir(string? workingDir) => GitHubRunCachePaths.GetCacheDir(_fs, workingDir);

    /// <summary>
    /// 将源 JsonElement 的指定属性原样复制到 Utf8JsonWriter — AOT 友好(无反射/emit)
    /// </summary>
    private static void CopyProperty(JsonElement source, Utf8JsonWriter writer, string name) {
        if (source.TryGetProperty(name, out var value)) {
            writer.WritePropertyName(name);
            writer.WriteRawValue(value.GetRawText());
        }
    }

    /// <summary>
    /// 解析 owner/repo — 优先用 repo 参数，否则从 git remote origin 推断（ADR 0073）
    /// </summary>
    private async Task<(string owner, string repo)?> ResolveOwnerRepoAsync(string? repo, string? workingDir, CancellationToken ct) {
        if (!string.IsNullOrWhiteSpace(repo)) {
            var parsed = ParseGitHubRepoRef(repo);
            if (parsed is not null) return parsed;
        }
        if (_git is null) return null;
        var gitResult = await _git.ExecuteAsync("remote get-url origin", workingDir, ct).ConfigureAwait(false);
        if (!gitResult.Success || string.IsNullOrWhiteSpace(gitResult.Output)) return null;
        return ParseGitHubRemoteUrl(gitResult.Output.Trim());
    }

    /// <summary>
    /// 解析 "owner/repo" 格式
    /// </summary>
    private static (string owner, string repo)? ParseGitHubRepoRef(string repo) {
        var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var owner = parts[0];
        var repoName = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return (owner, repoName);
    }

    /// <summary>
    /// 解析 GitHub remote URL — 支持 https://github.com/owner/repo.git 和 git@github.com:owner/repo.git
    /// </summary>
    private static (string owner, string repo)? ParseGitHubRemoteUrl(string url) {
        if (string.IsNullOrEmpty(url)) return null;
        string path;
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) {
            var uri = new Uri(url);
            path = uri.AbsolutePath.TrimStart('/');
        } else if (url.Contains('@')) {
            var colonIdx = url.IndexOf(':');
            if (colonIdx < 0) return null;
            path = url[(colonIdx + 1)..];
        } else return null;

        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        return (parts[0], parts[1]);
    }
}