namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 行范围偏移读取工具 — 统一 gh 日志/FileRead/rg 等工具的偏移读取+行号格式化+续读提示。
/// 对齐 TS: addLineNumbers + skip_lines 分页续读模式。
/// </summary>
public static class LineRangeReader {
    /// <summary>
    /// 纯截断 — 从行集合中按偏移读取指定范围，不格式化行号。
    /// 用于只需要截断列表、不需要行号格式化的场景（如 rg/grep 结果截断、ApplyLimit 泛型截断）。
    /// </summary>
    /// <param name="lines">全部行集合。</param>
    /// <param name="skipLines">跳过前 N 行（0-based）。</param>
    /// <param name="maxLines">最多读取行数。</param>
    /// <returns>(截断后的行, 是否还有更多行, 下次续读的 skip 值)</returns>
    public static (IReadOnlyList<T> Range, bool HasMore, int NextSkip) Slice<T>(
        IReadOnlyList<T> lines, int skipLines, int maxLines) {
        if (skipLines >= lines.Count)
            return (Array.Empty<T>(), false, skipLines);

        var take = Math.Min(lines.Count - skipLines, maxLines);
        var range = new List<T>(take);
        for (var i = skipLines; i < skipLines + take; i++) {
            range.Add(lines[i]);
        }

        var hasMore = skipLines + take < lines.Count;
        return (range, hasMore, skipLines + take);
    }

    /// <summary>
    /// 截断 + 行号格式化 + 续读提示。
    /// 行号用 <see cref="LineNumberFormatter"/> 格式化，续读提示包含 skip_lines 参数供 LLM 分页续读。
    /// </summary>
    /// <param name="lines">全部行集合。</param>
    /// <param name="skipLines">跳过前 N 行（0-based）。</param>
    /// <param name="maxLines">最多读取行数。</param>
    /// <param name="compactLinePrefix">行号前缀格式: true=紧凑 tab, false=箭头 →。</param>
    /// <returns>格式化文本 + 是否还有更多行 + 下次续读的 skip 值。</returns>
    public static LineRangeResult Read(
        IReadOnlyList<string> lines, int skipLines, int maxLines, bool compactLinePrefix) {
        if (lines.Count == 0)
            return new LineRangeResult(string.Empty, false, skipLines);

        var (range, hasMore, nextSkip) = Slice<string>(lines, skipLines, maxLines);
        if (range.Count == 0)
            return new LineRangeResult(
                $"已跳过全部 {lines.Count} 行(skip_lines={skipLines})，无更多内容。", false, skipLines);

        var sb = new StringBuilder(range.Count * 80);
        for (var i = 0; i < range.Count; i++) {
            sb.Append(LineNumberFormatter.Format(skipLines + i + 1, range[i], compactLinePrefix)).Append('\n');
        }

        if (hasMore) {
            sb.Append("... [共 ").Append(lines.Count)
              .Append(" 行，显示第 ").Append(skipLines + 1)
              .Append('-').Append(skipLines + range.Count)
              .Append(" 行。用 skip_lines=").Append(nextSkip)
              .Append(" 续读后续行]");
        }

        return new LineRangeResult(sb.ToString(), hasMore, nextSkip);
    }
}

/// <summary>
/// 行范围读取结果 — 格式化文本 + 是否还有更多行 + 下次续读的 skip 值。
/// </summary>
/// <param name="Text">格式化后的文本（含行号前缀+续读提示）。</param>
/// <param name="HasMore">是否还有更多行未读取。</param>
/// <param name="NextSkip">下次续读的 skip_lines 值（= 当前 skip + 已读取行数）。</param>
public sealed record LineRangeResult(string Text, bool HasMore, int NextSkip);
