namespace McpToolDispatch;

/// <summary>
/// GitHub Run 日志过滤与格式化工具 — 提供标记过滤、分页截断、前缀构建等静态方法
/// </summary>
internal static class GitHubRunLogFilter {
    /// <summary>
    /// 获取过滤级别对应的标记集 — 按 [Flags] 位标志组合,每位对应一类结构化标记
    /// </summary>
    public static FrozenSet<string> GetFilterMarkers(GitHubLogFilter filter) {
        if (filter == GitHubLogFilter.None) return FrozenSet<string>.Empty;
        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if ((filter & GitHubLogFilter.Error) != 0) markers.Add("##[error]");
        if ((filter & GitHubLogFilter.Warning) != 0) markers.Add("##[warning]");
        if ((filter & GitHubLogFilter.Command) != 0) markers.Add("##[command]");
        if ((filter & GitHubLogFilter.Failed) != 0) { markers.Add("[FAIL]"); markers.Add("  Failed "); }
        if ((filter & GitHubLogFilter.Exception) != 0) markers.Add("Exception:");
        return markers.ToFrozenSet();
    }

    /// <summary>
    /// 解析日志过滤级别字符串为 [Flags] 枚举 — 支持逗号分隔组合(如 "error,failed" → Error|Failed)
    /// <para>单值(如 "error"/"all")走 FromValue; 多值(如 "error,failed")拆分逐个 FromValue 再按位或</para>
    /// </summary>
    public static bool TryParseLogFilter(string? filter, out GitHubLogFilter result) {
        result = GitHubLogFilter.None;
        if (string.IsNullOrWhiteSpace(filter)) return false;
        var parts = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var combined = GitHubLogFilter.None;
        foreach (var part in parts) {
            var parsed = GitHubLogFilterExtensions.FromValue(part);
            if (parsed is null) return false;
            combined |= parsed.Value;
        }
        result = combined;
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetSectionPreview(List<string> lines) {
        if (lines.Count == 0) return string.Empty;
        var first = lines[0];
        return first.Length <= 60 ? first : string.Concat(first.AsSpan(0, 60), "...");
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
            sb.Append(i + 1).Append('\t').Append(lines[i]).Append('\n');
        }
        var hasMore = skipLines + take < lines.Count;
        if (hasMore) {
            sb.Append("... [共 ").Append(lines.Count).Append(" 行，显示第 ").Append(skipLines + 1).Append('-').Append(skipLines + take).Append(" 行。");
            sb.Append("用 skip_lines=").Append(skipLines + take).Append(" 续读后续行]");
        }
        return (sb.ToString(), hasMore);
    }

    /// <summary>
    /// 对日志行列表应用标记过滤
    /// </summary>
    public static List<string> ApplyFilter(List<string> lines, FrozenSet<string>? markers) {
        if (markers is null) return lines;
        if (lines.Count >= 1000)
            return lines.AsParallel().AsOrdered().Where(line => LineMatchesAnyMarkerInline(line, markers)).ToList();
        var result = new List<string>(lines.Count);
        foreach (var line in lines) {
            var lineSpan = line.AsSpan();
            foreach (var marker in markers) {
                if (lineSpan.Contains(marker, StringComparison.OrdinalIgnoreCase)) {
                    result.Add(line);
                    break;
                }
            }
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool LineMatchesAnyMarkerInline(string line, FrozenSet<string> markers) {
        var lineSpan = line.AsSpan();
        foreach (var marker in markers) {
            if (lineSpan.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
        var span = jobId.AsSpan();
        while (!span.IsEmpty) {
            var commaIdx = span.IndexOf(',');
            var part = commaIdx < 0 ? span : span[..commaIdx];
            span = commaIdx < 0 ? default : span[(commaIdx + 1)..];
            if (long.TryParse(part.Trim(), out var id))
                result.Add(id);
        }
        return result;
    }
}