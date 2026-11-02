namespace McpToolDispatch;

/// <summary>
/// 分页截断步骤 — 行数主约束(PageSize) + 字符安全阀(CharThreshold) 取交集
/// <para>收集所有行→判断是否截断→输出截断行→设置 ctx.TotalLines/ShownLines/HasMore</para>
/// <para>行完整不截断CI消息;字符安全阀防极端长行撑爆token</para>
/// </summary>
internal sealed class PaginationStep : ILogPipelineStep {
    /// <summary>分页截断: 行数主约束+字符安全阀取交集,设置 ctx.TotalLines/ShownLines/HasMore</summary>
    public async IAsyncEnumerable<string> ProcessAsync(LogContext ctx, IAsyncEnumerable<string> input,
        [EnumeratorCancellation] CancellationToken ct) {
        ctx.EffectiveSkip = ctx.StartLine.HasValue ? ctx.StartLine.Value - 1 : ctx.SkipLines ?? 0;

        // 收集所有行(分页需要总行数)
        var lines = new List<string>();
        await foreach (var line in input.ConfigureAwait(false)) {
            ct.ThrowIfCancellationRequested();
            lines.Add(line);
        }
        ctx.TotalLines = lines.Count;

        // 应用 skip_lines/start_line 偏移
        var start = Math.Min(ctx.EffectiveSkip, lines.Count);
        var remaining = lines.Count - start;

        // 未超行数主约束 且 未超字符安全阀 → 全量输出
        var totalChars = 0;
        for (var i = start; i < lines.Count; i++) totalChars += lines[i].Length + 1;

        if (remaining <= ctx.PageSize && totalChars <= ctx.CharThreshold) {
            ctx.ShownLines = remaining;
            ctx.HasMore = false;
            for (var i = start; i < lines.Count; i++) yield return lines[i];
            yield break;
        }

        // 截断: 行数主约束 与 字符安全阀 取交集
        var maxLines = ComputeMaxLines(lines, start, ctx.PageSize, ctx.CharThreshold);
        ctx.ShownLines = maxLines;
        ctx.HasMore = remaining > maxLines;
        for (var i = start; i < start + maxLines; i++) yield return lines[i];
    }

    /// <summary>计算字符预算内可显示的最大行数 — 行数主约束 与 字符安全阀 取交集</summary>
    private static int ComputeMaxLines(List<string> lines, int start, int pageSize, int charThreshold) {
        var limit = Math.Min(pageSize, lines.Count - start);
        var accumChars = 0;
        for (var i = start; i < start + limit; i++) {
            accumChars += lines[i].Length + 1;
            if (accumChars > charThreshold) return Math.Max(i - start, 1);
        }
        return limit;
    }
}
