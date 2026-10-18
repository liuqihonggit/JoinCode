namespace McpToolDispatch;

/// <summary>
/// 页眉步骤 — 在输出开头插入 │ toolName │ 页 N/M │ 行 X-Y │ 偏移 Z │
/// <para>read_offset 模式始终显示页眉(截断/未截断都显示)</para>
/// </summary>
internal sealed class PageHeaderStep : ILogPipelineStep {
    /// <summary>在输出开头插入页眉 │ toolName │ 页 N/M │ 行 X-Y │ 偏移 Z │</summary>
    public async IAsyncEnumerable<string> ProcessAsync(LogContext ctx, IAsyncEnumerable<string> input,
        [EnumeratorCancellation] CancellationToken ct) {
        if (ctx.AddPageHeader && !string.IsNullOrEmpty(ctx.ToolName) && ctx.ShownLines > 0) {
            var totalPages = ctx.ShownLines > 0 ? (int)Math.Ceiling((double)ctx.TotalLines / ctx.ShownLines) : 1;
            var currentPage = ctx.ShownLines > 0 ? (ctx.EffectiveSkip / ctx.ShownLines) + 1 : 1;
            var sb = new StringBuilder(128);
            sb.Append("│ ").Append(ctx.ToolName).Append(" │ 页 ").Append(currentPage).Append('/').Append(totalPages)
              .Append(" │ 行 ").Append(ctx.EffectiveSkip + 1).Append('-').Append(ctx.EffectiveSkip + ctx.ShownLines)
              .Append(" │ 偏移 ").Append(ctx.EffectiveSkip).Append(" │");
            yield return sb.ToString();
        }
        await foreach (var line in input.ConfigureAwait(false)) yield return line;
    }
}
