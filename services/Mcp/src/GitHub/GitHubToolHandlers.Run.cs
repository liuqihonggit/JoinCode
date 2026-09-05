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

    [McpTool(GitHubToolNameConstants.GhRunView, "查看 Run 详情/日志(expand 按步骤展开+MemoryCache缓存,filter 按标记过滤,skip_lines 分页续读)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhRunViewAsync(
        [McpToolParameter("Run ID", Required = true)] string run_id,
        [McpToolParameter("Job ID(可选,精准拉单 job)", Required = false)] string? job_id = null,
        [McpToolParameter("是否拉取日志(默认 false,仅看详情)", Required = false)] bool? log = null,
        [McpToolParameter("最大日志行数(默认 200)", Required = false)] int? max_lines = null,
        [McpToolParameter("跳过前 N 行(用于续读截断日志,默认 0)", Required = false)] int? skip_lines = null,
        [McpToolParameter("按步骤展开: steps=列出步骤列表, failed=只拉失败步骤, step:Name=只拉指定步骤(复刻 ToolSearch map[] 逐层drill down)", Required = false)] string? expand = null,
        [McpToolParameter("日志过滤级别(error/warning/info/all,默认 all=不过滤)", Required = false)] string? filter = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var maxLines = max_lines ?? 200;
        var skip = skip_lines ?? 0;
        var hasFilter = TryParseLogFilter(filter, out var filterLevel) && filterLevel != GitHubLogFilter.All;
        var markers = hasFilter ? GetFilterMarkers(filterLevel) : null;

        // === expand=failed: 用 --log-failed 只拉失败步骤(量少,不缓存) ===
        if (string.Equals(expand, "failed", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder($"run view {run_id} --log-failed");
            if (!string.IsNullOrWhiteSpace(job_id)) sb.Append($" --job {job_id}");
            return await StreamAndFilterAsync(sb.ToString(), run_id, "失败步骤", working_dir, markers, filterLevel, maxLines, cancellationToken, FailedHint, skip);
        }

        // === expand=steps 或 expand=step:Name: 两级缓存(ADR 0067) ===
        var expandStep = expand?.StartsWith("step:", StringComparison.OrdinalIgnoreCase) == true
            ? expand[5..].Trim()
            : null;
        var wantSteps = string.Equals(expand, "steps", StringComparison.OrdinalIgnoreCase);

        if (wantSteps || expandStep is not null)
        {
            // 解析 /section:Type 后缀
            string? sectionType = null;
            if (expandStep is not null)
            {
                var sectionIdx = expandStep.IndexOf("/section:", StringComparison.OrdinalIgnoreCase);
                if (sectionIdx >= 0)
                {
                    sectionType = expandStep[(sectionIdx + 9)..].Trim();
                    expandStep = expandStep[..sectionIdx].Trim();
                }
            }

            // expand=step:Name/section:Type: 从 Level2 内容缓存读取(ADR 0067)
            if (expandStep is not null && sectionType is not null)
            {
                var sectionLines = await GetOrFetchSectionAsync(run_id, job_id, expandStep, sectionType, working_dir, cancellationToken);
                if (sectionLines is null)
                    return Ok($"未找到步骤 '{expandStep}' 或 section '{sectionType}'，建议先 expand=step:{expandStep} 查看 section 摘要");

                var (secText, secHasMore) = SkipAndTruncate(sectionLines, maxLines, skip);
                if (secHasMore)
                    secText += TruncatedHint;
                var secPrefix = BuildPrefix(run_id, $"步骤:{expandStep}/section:{sectionType}", filterLevel, sectionLines.Count);
                return Ok(secText, secPrefix);
            }

            // 其余情况(expand=steps 或 expand=step:Name): 从 Level1 摘要缓存读取
            var summary = await GetOrFetchSummaryAsync(run_id, job_id, working_dir, cancellationToken);
            if (summary is null) return Fail("日志拉取失败");

            // expand=steps: 返回步骤列表
            if (wantSteps)
            {
                var stepsText = summary.StepLineCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Select(kvp => $"  {kvp.Value,6} 行  {kvp.Key}");
                return Ok(string.Join('\n', stepsText) + StepsHint, $"Run {run_id} 步骤列表({summary.StepLineCounts.Count} 步骤,缓存于 {summary.CachedAt:HH:mm:ss}):");
            }

            // expand=step:Name: 返回 section 摘要(Level 2,ADR 0067)
            if (expandStep is not null)
            {
                if (!summary.SectionCounts.TryGetValue(expandStep, out var secs))
                    return Ok($"未找到步骤 '{expandStep}'，可用步骤: {string.Join(", ", summary.SectionCounts.Keys)}");

                var summaryText = secs
                    .OrderBy(kvp => SectionOrder(kvp.Key))
                    .Select(kvp => $"  {kvp.Key,-8} {kvp.Value,5} 行  (用 expand=step:{expandStep}/section:{kvp.Key} 查看)");
                return Ok(string.Join('\n', summaryText) + SectionHint, $"Run {run_id} 步骤:{expandStep} sections({secs.Count} 类):");
            }
        }

        // === 常规模式: log=false 看摘要, log=true 拉全部日志 ===
        var wantLog = log == true;
        var sb2 = new StringBuilder($"run view {run_id}");
        if (!string.IsNullOrWhiteSpace(job_id)) sb2.Append($" --job {job_id}");
        if (wantLog) sb2.Append(" --log");

        if (wantLog && (hasFilter || expand is not null))
            return await StreamAndFilterAsync(sb2.ToString(), run_id, "流式过滤", working_dir, markers, filterLevel, maxLines, cancellationToken, LogHint, skip);

        var result = await RunGhAsync(sb2.ToString(), ResolveWorkDir(working_dir), cancellationToken, wantLog ? 120_000 : null);
        if (!result.Success)
        {
            // 超时时追加纵深防御提示,避免 AI 改用 gh api
            if (IsTimeoutError(result))
                return Fail(result.Error + TimeoutDefenseHint);
            return Fail(result);
        }

        if (wantLog)
        {
            var allLines = result.Output.Split('\n');
            var (text, hasMore) = SkipAndTruncate(allLines, maxLines, skip);
            if (hasMore)
                text += TruncatedHint;
            return Ok(text + LogHint, $"Run {run_id} 日志:");
        }
        return Ok(result.Output);
    }

    /// <summary>
    /// 从 Level1 摘要缓存获取或流式拉取 — 返回 RunLogSummary(步骤名→行数, section类型→行数)
    /// <para>未命中时流式拉取 --log,构建 Level1 摘要 + 同时把每个 section 的日志行存入 Level2 缓存</para>
    /// <para>key=nameof(GitHubToolHandlers)+":summary:{runId}:{jobId}", 24h 过期</para>
    /// <para>ADR 0067 两级缓存: 摘要(轻量)长期保留,内容(大量行)按 section 独立缓存可被驱逐</para>
    /// </summary>
    private async Task<RunLogSummary?> GetOrFetchSummaryAsync(string runId, string? jobId, string? workingDir, CancellationToken ct)
    {
        var summaryKey = $"{_summaryPrefix}{runId}:{jobId ?? "all"}";
        if (_logCache.Get(summaryKey) is RunLogSummary cachedSummary)
        {
            _logger?.LogDebug("Level1 摘要缓存命中: {Key}", summaryKey);
            return cachedSummary;
        }

        var sb = new StringBuilder($"run view {runId} --log");
        if (!string.IsNullOrWhiteSpace(jobId)) sb.Append($" --job {jobId}");

        var summary = new RunLogSummary { RunId = runId, JobId = jobId };
        // 临时收集: stepName → (sectionType → lines),拉取完写入 Level2 缓存
        var sectionContents = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

        await foreach (var line in _gh.ExecuteStreamingAsync(sb.ToString(), ResolveWorkDir(workingDir), 120_000, ct).ConfigureAwait(false))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var stepName = parts[1];
            var sectionType = RunLogCache.ParseSectionType(line);

            // Level1 摘要: 步骤行数
            summary.StepLineCounts[stepName] = summary.StepLineCounts.GetValueOrDefault(stepName) + 1;

            // Level1 摘要: section 计数
            if (!summary.SectionCounts.TryGetValue(stepName, out var secCounts))
            {
                secCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                summary.SectionCounts[stepName] = secCounts;
            }
            secCounts[sectionType] = secCounts.GetValueOrDefault(sectionType) + 1;

            // Level2 内容: 临时收集日志行
            if (!sectionContents.TryGetValue(stepName, out var stepSecs))
            {
                stepSecs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                sectionContents[stepName] = stepSecs;
            }
            if (!stepSecs.TryGetValue(sectionType, out var secLines))
            {
                secLines = new List<string>();
                stepSecs[sectionType] = secLines;
            }
            secLines.Add(line);
        }

        // 写入 Level2 内容缓存(每个 section 独立 key,24h 过期)
        foreach (var (stepName, stepSecs) in sectionContents)
        {
            foreach (var (secType, secLines) in stepSecs)
            {
                var sectionKey = $"{_sectionPrefix}{runId}:{jobId ?? "all"}:{stepName}:{secType}";
                _logCache.Add(sectionKey, secLines, DateTimeOffset.Now.AddHours(24));
            }
        }

        // 写入 Level1 摘要缓存
        _logCache.Add(summaryKey, summary, DateTimeOffset.Now.AddHours(24));
        _logger?.LogDebug("Level1 摘要已缓存: {Key}, {Steps} 步骤, {Sections} sections",
            summaryKey, summary.StepLineCounts.Count, sectionContents.Count);
        return summary;
    }

    /// <summary>
    /// 从 Level2 内容缓存获取指定 section 的日志行 — 未命中时先确保 Level1 摘要已构建(会填充所有 Level2)
    /// <para>key=nameof(GitHubToolHandlers)+":section:{runId}:{jobId}:{stepName}:{sectionType}", 24h 过期</para>
    /// <para>内存压力时 Level2 可被独立驱逐,下次访问时按需重新拉取(通过 Level1 触发全量重建)</para>
    /// </summary>
    private async Task<List<string>?> GetOrFetchSectionAsync(
        string runId, string? jobId, string stepName, string sectionType,
        string? workingDir, CancellationToken ct)
    {
        var sectionKey = $"{_sectionPrefix}{runId}:{jobId ?? "all"}:{stepName}:{sectionType}";
        if (_logCache.Get(sectionKey) is List<string> cachedLines)
        {
            _logger?.LogDebug("Level2 内容缓存命中: {Key}, {Lines} 行", sectionKey, cachedLines.Count);
            return cachedLines;
        }

        // Level2 未命中,先确保 Level1 已构建(会同时填充所有 Level2 缓存)
        await GetOrFetchSummaryAsync(runId, jobId, workingDir, ct).ConfigureAwait(false);

        // 再次从 Level2 读取
        if (_logCache.Get(sectionKey) is List<string> lines)
        {
            _logger?.LogDebug("Level2 内容缓存(填充后)命中: {Key}, {Lines} 行", sectionKey, lines.Count);
            return lines;
        }

        // 仍然未命中,说明该 step/section 不存在
        _logger?.LogDebug("Level2 内容缓存未命中(步骤/section 不存在): {Key}", sectionKey);
        return null;
    }

    /// <summary>
    /// 流式拉取 + 过滤 + 分页跳过(不缓存,用于 --log-failed 或一次性过滤)
    /// </summary>
    private async Task<ToolResult> StreamAndFilterAsync(
        string command, string runId, string scope, string? workingDir,
        FrozenSet<string>? markers, GitHubLogFilter? filterLevel,
        int maxLines, CancellationToken ct, string? hint = null, int skipLines = 0)
    {
        var matched = new List<string>(maxLines);
        var skipped = 0;
        await foreach (var line in _gh.ExecuteStreamingAsync(command, ResolveWorkDir(workingDir), 120_000, ct).ConfigureAwait(false))
        {
            if (markers is not null && !markers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase)))
                continue;
            // 先跳过 skipLines 行(分页续读)
            if (skipped < skipLines) { skipped++; continue; }
            matched.Add(line);
            if (matched.Count >= maxLines) break;
        }
        var prefix = BuildPrefix(runId, scope, filterLevel, matched.Count);
        if (matched.Count == 0)
            return Ok(skipLines > 0 ? $"未匹配到更多日志行(已跳过 {skipLines} 行)" : "未匹配到任何日志行", prefix);
        var text = string.Join('\n', matched);
        // 达到 maxLines 说明可能还有更多行,追加续读提示
        if (matched.Count >= maxLines)
            text += $"\n... [可能还有更多行，用 skip_lines={skipLines + maxLines} 续读]";
        if (hint is not null) text += hint;
        return Ok(text, prefix);
    }

    /// <summary>
    /// 跳过前 skipLines 行,再截断到 maxLines 行 — 返回 (结果文本, 是否还有更多行)
    /// <para>截断提示包含 skip_lines 续读参数,LLM 可直接分页获取后续行</para>
    /// </summary>
    private static (string text, bool hasMore) SkipAndTruncate(IReadOnlyList<string> lines, int maxLines, int skipLines)
    {
        if (lines.Count == 0) return (string.Empty, false);
        if (skipLines >= lines.Count)
            return ($"已跳过全部 {lines.Count} 行(skip_lines={skipLines})，无更多日志。", false);

        var take = Math.Min(lines.Count - skipLines, maxLines);
        var sb = new StringBuilder(take * 80);
        for (int i = skipLines; i < skipLines + take; i++)
        {
            sb.Append(lines[i]);
            sb.Append('\n');
        }
        var hasMore = skipLines + take < lines.Count;
        if (hasMore)
        {
            sb.Append($"... [共 {lines.Count} 行，显示第 {skipLines + 1}-{skipLines + take} 行。");
            sb.Append($"用 skip_lines={skipLines + take} 续读后续行]");
        }
        return (sb.ToString(), hasMore);
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
    /// expand=step:Name 返回 section 摘要后的下一步提示(ADR 0067 Level 2)
    /// </summary>
    private const string SectionHint =
        "\n\n💡 下一步:\n" +
        "- /section:error → 查看 error 段(排障首要)\n" +
        "- /section:group → 查看 group 段(命令上下文)\n" +
        "- /section:normal → 查看普通日志(测试结果)";

    /// <summary>
    /// 日志被截断时的缩小范围提示
    /// </summary>
    private const string TruncatedHint =
        "\n\n💡 日志已截断，缩小范围:\n" +
        "- skip_lines=N → 续读后续行(截断提示中有具体值)\n" +
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
        "- filter=error → 只看错误行\n" +
        "- skip_lines=N → 分页续读";

    /// <summary>
    /// 超时纵深防御提示 — 引导 AI 用 gh 工具的降级路径,不要改用 gh api
    /// </summary>
    private const string TimeoutDefenseHint =
        "\n\n💡 日志量大导致超时，不要改用 gh api，用以下方式缩小范围:\n" +
        "1. expand=steps → 按步骤展开(每步单独拉取,量小)\n" +
        "2. expand=step:步骤名 → 只拉指定步骤\n" +
        "3. skip_lines=N + max_lines=M → 分页拉取\n" +
        "4. job_id=xxx → 精准拉单 job";

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

    /// <summary>
    /// 判断是否为超时错误 — 用于触发纵深防御提示(避免 AI 改用 gh api)
    /// </summary>
    private static bool IsTimeoutError(GitHubCommandResult result)
    {
        if (string.IsNullOrEmpty(result.Error)) return false;
        return result.Error.Contains("超时", StringComparison.OrdinalIgnoreCase)
            || result.Error.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || result.Error.Contains("timed out", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Section 类型的排序优先级 — error 优先(排障首要),normal 最后
    /// </summary>
    private static int SectionOrder(string type) => type switch
    {
        RunLogCache.SectionError => 0,
        RunLogCache.SectionWarning => 1,
        RunLogCache.SectionCommand => 2,
        RunLogCache.SectionGroup => 3,
        RunLogCache.SectionNormal => 4,
        _ => 5,
    };

    /// <summary>
    /// 获取 section 的预览文本 — 第一行截断到 60 字符
    /// </summary>
    private static string GetSectionPreview(List<string> lines)
    {
        if (lines.Count == 0) return string.Empty;
        var first = lines[0];
        return first.Length <= 60 ? first : first[..60] + "...";
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
