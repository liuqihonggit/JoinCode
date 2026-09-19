namespace McpToolDispatch;

/// <summary>
/// GitHub Run 日志过滤与格式化工具 — 提供标记过滤、分页截断、前缀构建等静态方法
/// </summary>
internal static class GitHubRunLogFilter {
    /// <summary>
    /// GitHub Actions 日志过滤标记集 — 按 <see cref="GitHubLogFilter"/> 级别匹配 ##[error] / ##[warning] / ##[command]
    /// </summary>
    public static readonly FrozenSet<string> ErrorMarkers = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "##[error]");

    public static readonly FrozenSet<string> WarningMarkers = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "##[error]", "##[warning]");

    public static readonly FrozenSet<string> InfoMarkers = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "##[error]", "##[warning]", "##[command]");

    /// <summary>
    /// 获取过滤级别对应的标记集
    /// </summary>
    public static FrozenSet<string> GetFilterMarkers(GitHubLogFilter filter) => filter switch {
        GitHubLogFilter.Error => ErrorMarkers,
        GitHubLogFilter.Warning => WarningMarkers,
        GitHubLogFilter.Info => InfoMarkers,
        _ => ErrorMarkers,
    };

    /// <summary>
    /// 解析日志过滤级别字符串为枚举 — 无效值返回 false(走常规模式)
    /// </summary>
    public static bool TryParseLogFilter(string? filter, out GitHubLogFilter result) {
        result = GitHubLogFilter.All;
        if (string.IsNullOrWhiteSpace(filter)) return false;
        var parsed = GitHubLogFilterExtensions.FromValue(filter);
        if (parsed is null) return false;
        result = parsed.Value;
        return true;
    }

    /// <summary>
    /// 检测日志文本是否只有 "Process completed with exit code N" 但缺少栈帧信息
    /// <para>栈帧标记: "  at " / "Exception" / "StackTrace" / "   at " — 有任一即视为有栈帧</para>
    /// </summary>
    public static bool HasNoStackTrace(string text) {
        if (!text.Contains("Process completed with exit code", StringComparison.OrdinalIgnoreCase))
            return false;
        return !text.Contains("  at ", StringComparison.Ordinal)
            && !text.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("StackTrace", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("stack trace", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Section 类型的排序优先级 — error 优先(排障首要),normal 最后
    /// </summary>
    public static int SectionOrder(string type) => type switch {
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
    public static string GetSectionPreview(List<string> lines) {
        if (lines.Count == 0) return string.Empty;
        var first = lines[0];
        return first.Length <= 60 ? first : first[..60] + "...";
    }

    /// <summary>
    /// 跳过前 skipLines 行,再截断到 maxLines 行 — 返回 (结果文本, 是否还有更多行)
    /// <para>截断提示包含 skip_lines 续读参数,LLM 可直接分页获取后续行</para>
    /// </summary>
    public static (string text, bool hasMore) SkipAndTruncate(IReadOnlyList<string> lines, int maxLines, int skipLines) {
        if (lines.Count == 0) return (string.Empty, false);
        if (skipLines >= lines.Count)
            return ($"已跳过全部 {lines.Count} 行(skip_lines={skipLines})，无更多日志。", false);

        var take = Math.Min(lines.Count - skipLines, maxLines);
        var sb = new StringBuilder(take * 80);
        for (var i = skipLines; i < skipLines + take; i++) {
            sb.Append(lines[i]);
            sb.Append('\n');
        }
        var hasMore = skipLines + take < lines.Count;
        if (hasMore) {
            sb.Append($"... [共 {lines.Count} 行，显示第 {skipLines + 1}-{skipLines + take} 行。");
            sb.Append($"用 skip_lines={skipLines + take} 续读后续行]");
        }
        return (sb.ToString(), hasMore);
    }

    /// <summary>
    /// 对日志行列表应用标记过滤
    /// </summary>
    public static List<string> ApplyFilter(List<string> lines, FrozenSet<string>? markers) {
        if (markers is null) return lines;
        return lines.Where(l => markers.Any(m => l.Contains(m, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>
    /// 构建结果前缀
    /// </summary>
    public static string BuildPrefix(string runId, string scope, GitHubLogFilter? filterLevel, int count) {
        var parts = new List<string> { scope };
        if (filterLevel is { } fl) parts.Add($"过滤:{fl.ToValue()}");
        return $"Run {runId} 日志({string.Join(", ", parts)},匹配 {count} 行):";
    }

    /// <summary>
    /// 解析逗号分隔的 job IDs 字符串(如 "123,456")为 List{long}
    /// </summary>
    public static List<long> ParseJobIds(string? jobId) {
        if (string.IsNullOrWhiteSpace(jobId)) return [];
        var result = new List<long>();
        foreach (var part in jobId.Split(',')) {
            if (long.TryParse(part.Trim(), out var id))
                result.Add(id);
        }
        return result;
    }
}