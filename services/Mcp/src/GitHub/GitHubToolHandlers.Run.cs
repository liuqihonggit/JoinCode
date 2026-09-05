namespace McpToolDispatch;

/// <summary>
/// GitHub Actions Run 工具 — gh run 子命令全套
/// <para>避坑2/3/5: 大日志 maxLines 截断 + --job 精准拉 + 30s 超时</para>
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhRunList, "列出 Actions Run(支持状态/分支过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunListAsync(
        [McpToolParameter("数量限制(默认 20)", Required = false)] int? limit = null,
        [McpToolParameter("状态过滤(queued/in_progress/completed,可选)", Required = false)] string? status = null,
        [McpToolParameter("分支过滤(可选)", Required = false)] string? branch = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder("run list --json databaseId,status,conclusion,headBranch,event,workflowName,createdAt");
        sb.Append($" --limit {limit ?? 20}");
        if (!string.IsNullOrWhiteSpace(status)) sb.Append($" --status {status}");
        if (!string.IsNullOrWhiteSpace(branch)) sb.Append($" --branch {branch}");
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        if (!result.Success) return Fail(result);

        // 发现失败的 run 时追加排障步骤提示
        var hasFailure = result.Output.Contains("\"conclusion\":\"failure\"", StringComparison.OrdinalIgnoreCase);
        return hasFailure ? Ok(result.Output + RunListFailureHint) : Ok(result.Output);
    }

    [McpTool(GitHubToolNameConstants.GhRunView, "查看 Run 详情/日志(expand 按步骤展开+LRU缓存,filter 按标记过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunViewAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("Job ID(可选,精准拉单 job)", Required = false)] string? job_id = null,
        [McpToolParameter("是否拉取日志(默认 false,仅看详情)", Required = false)] bool? log = null,
        [McpToolParameter("最大日志行数(默认 200)", Required = false)] int? max_lines = null,
        [McpToolParameter("按步骤展开: steps=列出步骤列表, failed=只拉失败步骤, step:Name=只拉指定步骤(复刻 ToolSearch map[] 逐层drill down)", Required = false)] string? expand = null,
        [McpToolParameter("日志过滤级别(error/warning/info/all,默认 all=不过滤)", Required = false)] string? filter = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var maxLines = max_lines ?? 200;
        var hasFilter = TryParseLogFilter(filter, out var filterLevel) && filterLevel != GitHubLogFilter.All;
        var markers = hasFilter ? GetFilterMarkers(filterLevel) : null;

        // === expand=failed: 用 --log-failed 只拉失败步骤(量少,不缓存) ===
        if (string.Equals(expand, "failed", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder($"run view {run_id} --log-failed");
            if (!string.IsNullOrWhiteSpace(job_id)) sb.Append($" --job {job_id}");
            return await StreamAndFilterAsync(sb.ToString(), run_id, "失败步骤", working_dir, markers, filterLevel, maxLines, cancellationToken, FailedHint);
        }

        // === expand=steps 或 expand=step:Name: 从缓存或流式拉取,按步骤展开 ===
        var expandStep = expand?.StartsWith("step:", StringComparison.OrdinalIgnoreCase) == true
            ? expand[5..].Trim()
            : null;
        var wantSteps = string.Equals(expand, "steps", StringComparison.OrdinalIgnoreCase);

        if (wantSteps || expandStep is not null)
        {
            var cache = await GetOrFetchCacheAsync(run_id, job_id, working_dir, cancellationToken);
            if (cache is null) return Fail("日志拉取失败");

            // expand=steps: 返回步骤列表
            if (wantSteps)
            {
                var summary = cache.Steps
                    .OrderByDescending(kvp => kvp.Value.Count)
                    .Select(kvp => $"  {kvp.Value.Count,6} 行  {kvp.Key}");
                return Ok(string.Join('\n', summary) + StepsHint, $"Run {run_id} 步骤列表({cache.Steps.Count} 步骤,缓存于 {cache.CachedAt:HH:mm:ss}):");
            }

            // expand=step:Name: 从缓存返回指定步骤的日志
            if (expandStep is not null)
            {
                if (!cache.Steps.TryGetValue(expandStep, out var stepLines))
                    return Ok($"未找到步骤 '{expandStep}'，可用步骤: {string.Join(", ", cache.Steps.Keys)}");

                var filtered = ApplyFilter(stepLines, markers);
                var truncated = TruncateLines(string.Join('\n', filtered), maxLines);
                // 被截断时追加缩小范围提示
                if (truncated.Contains("已截断", StringComparison.OrdinalIgnoreCase))
                    truncated += TruncatedHint;
                var prefix = BuildPrefix(run_id, $"步骤:{expandStep}", filterLevel, filtered.Count);
                return Ok(truncated, prefix);
            }
        }

        // === 常规模式: log=false 看摘要, log=true 拉全部日志 ===
        var wantLog = log == true;
        var sb2 = new StringBuilder($"run view {run_id}");
        if (!string.IsNullOrWhiteSpace(job_id)) sb2.Append($" --job {job_id}");
        if (wantLog) sb2.Append(" --log");

        if (wantLog && (hasFilter || expand is not null))
            return await StreamAndFilterAsync(sb2.ToString(), run_id, "流式过滤", working_dir, markers, filterLevel, maxLines, cancellationToken, LogHint);

        var result = await RunGhAsync(sb2.ToString(), ResolveWorkDir(working_dir), cancellationToken, wantLog ? 120_000 : null);
        if (!result.Success) return Fail(result);

        if (wantLog)
        {
            var truncated = TruncateLines(result.Output, maxLines);
            return Ok(truncated + LogHint, $"Run {run_id} 日志:");
        }
        return Ok(result.Output);
    }

    /// <summary>
    /// 从 MemoryCache 获取或流式拉取并缓存 — 按步骤分组存储
    /// <para>key=nameof(GitHubToolHandlers)+":{runId}:{jobId}", 24h 过期,内存压力自动释放</para>
    /// </summary>
    private async Task<RunLogCache?> GetOrFetchCacheAsync(string runId, string? jobId, string? workingDir, CancellationToken ct)
    {
        var cacheKey = $"{_cachePrefix}{runId}:{jobId ?? "all"}";
        if (_logCache.Get(cacheKey) is RunLogCache cached)
        {
            _logger?.LogDebug("Run 日志缓存命中: {Key}", cacheKey);
            return cached;
        }

        var sb = new StringBuilder($"run view {runId} --log");
        if (!string.IsNullOrWhiteSpace(jobId)) sb.Append($" --job {jobId}");

        var cache = new RunLogCache { RunId = runId, JobId = jobId };
        await foreach (var line in _gh.ExecuteStreamingAsync(sb.ToString(), ResolveWorkDir(workingDir), 120_000, ct).ConfigureAwait(false))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var stepName = parts[1];
            if (!cache.Steps.TryGetValue(stepName, out var lines))
            {
                lines = new List<string>();
                cache.Steps[stepName] = lines;
            }
            lines.Add(line);
        }

        _logCache.Add(cacheKey, cache, DateTimeOffset.Now.AddHours(24));
        _logger?.LogDebug("Run 日志已缓存: {Key}, {Steps} 步骤", cacheKey, cache.Steps.Count);
        return cache;
    }

    /// <summary>
    /// 流式拉取 + 过滤(不缓存,用于 --log-failed 或一次性过滤)
    /// </summary>
    private async Task<ToolResult> StreamAndFilterAsync(
        string command, string runId, string scope, string? workingDir,
        FrozenSet<string>? markers, GitHubLogFilter? filterLevel,
        int maxLines, CancellationToken ct, string? hint = null)
    {
        var matched = new List<string>(maxLines);
        await foreach (var line in _gh.ExecuteStreamingAsync(command, ResolveWorkDir(workingDir), 120_000, ct).ConfigureAwait(false))
        {
            if (markers is not null && !markers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase)))
                continue;
            matched.Add(line);
            if (matched.Count >= maxLines) break;
        }
        var prefix = BuildPrefix(runId, scope, filterLevel, matched.Count);
        if (matched.Count == 0)
            return Ok("未匹配到任何日志行", prefix);
        var text = string.Join('\n', matched);
        if (hint is not null) text += hint;
        return Ok(text, prefix);
    }

    /// <summary>
    /// 对日志行列表应用标记过滤
    /// </summary>
    private static List<string> ApplyFilter(List<string> lines, FrozenSet<string>? markers)
    {
        if (markers is null) return lines;
        return lines.Where(l => markers.Any(m => l.Contains(m, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>
    /// 构建结果前缀
    /// </summary>
    private static string BuildPrefix(string runId, string scope, GitHubLogFilter? filterLevel, int count)
    {
        var parts = new List<string> { scope };
        if (filterLevel is { } fl) parts.Add($"过滤:{fl.ToValue()}");
        return $"Run {runId} 日志({string.Join(", ", parts)},匹配 {count} 行):";
    }

    // === 排障提示词 — 引导用户逐步缩小范围(嵌入工具返回结果) ===

    /// <summary>
    /// gh_run_list 发现失败 run 时的排障步骤提示
    /// </summary>
    private const string RunListFailureHint =
        "\n\n💡 排障步骤:\n" +
        "1. gh_run_view run_id=xxx expand=steps → 查看步骤列表\n" +
        "2. gh_run_view run_id=xxx expand=failed → 只看失败步骤\n" +
        "3. gh_run_view run_id=xxx expand=step:步骤名 filter=error → 看具体步骤的错误行";

    /// <summary>
    /// expand=steps 返回步骤列表后的下一步提示
    /// </summary>
    private const string StepsHint =
        "\n\n💡 下一步:\n" +
        "- expand=failed → 只拉失败步骤(量少)\n" +
        "- expand=step:步骤名 → 查看具体步骤日志\n" +
        "- filter=error → 只看 ##[error] 标记行";

    /// <summary>
    /// 日志被截断时的缩小范围提示
    /// </summary>
    private const string TruncatedHint =
        "\n\n💡 日志已截断，缩小范围:\n" +
        "- filter=error → 只看错误行\n" +
        "- 增大 max_lines → 看更多行";

    /// <summary>
    /// expand=failed 返回失败步骤后的下一步提示
    /// </summary>
    private const string FailedHint =
        "\n\n💡 下一步: expand=step:步骤名 → 查看具体步骤的完整日志";

    /// <summary>
    /// 常规 log=true 模式的建议提示
    /// </summary>
    private const string LogHint =
        "\n\n💡 日志量大时建议:\n" +
        "- expand=steps → 按步骤展开\n" +
        "- filter=error → 只看错误行";

    /// <summary>
    /// GitHub Actions 日志过滤标记集 — 按 <see cref="GitHubLogFilter"/> 级别匹配 ##[error] / ##[warning] / ##[command]
    /// </summary>
    private static readonly FrozenSet<string> ErrorMarkers = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "##[error]");

    private static readonly FrozenSet<string> WarningMarkers = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "##[error]", "##[warning]");

    private static readonly FrozenSet<string> InfoMarkers = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "##[error]", "##[warning]", "##[command]");

    /// <summary>
    /// 获取过滤级别对应的标记集
    /// </summary>
    private static FrozenSet<string> GetFilterMarkers(GitHubLogFilter filter) => filter switch
    {
        GitHubLogFilter.Error => ErrorMarkers,
        GitHubLogFilter.Warning => WarningMarkers,
        GitHubLogFilter.Info => InfoMarkers,
        _ => ErrorMarkers,
    };

    /// <summary>
    /// 解析日志过滤级别字符串为枚举 — 无效值返回 false(走常规模式)
    /// </summary>
    private static bool TryParseLogFilter(string? filter, out GitHubLogFilter result)
    {
        result = GitHubLogFilter.All;
        if (string.IsNullOrWhiteSpace(filter)) return false;
        var parsed = GitHubLogFilterExtensions.FromValue(filter);
        if (parsed is null) return false;
        result = parsed.Value;
        return true;
    }

    [McpTool(GitHubToolNameConstants.GhRunRerun, "重跑 Actions Run(默认只重跑失败的 job)", "github")]
    public async Task<ToolResult> GhRunRerunAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("是否只重跑失败的 job(默认 true)", Required = false)] bool? failed_only = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"run rerun {run_id}");
        if (failed_only != false) sb.Append(" --failed");
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已重跑 Run {run_id}") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhRunCancel, "取消 Actions Run", "github")]
    public async Task<ToolResult> GhRunCancelAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync($"run cancel {run_id}", ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已取消 Run {run_id}") : Fail(result);
    }
}
