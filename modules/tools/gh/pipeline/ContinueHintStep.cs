namespace McpToolDispatch;

/// <summary>
/// 续读提示步骤 — 截断时在末尾加续读提示
/// </summary>
internal sealed class ContinueHintStep : ILogPipelineStep {
    /// <summary>截断时在末尾加续读提示 --skip_lines N</summary>
    public async IAsyncEnumerable<string> ProcessAsync(LogContext ctx, IAsyncEnumerable<string> input,
        [EnumeratorCancellation] CancellationToken ct) {
        await foreach (var line in input.ConfigureAwait(false)) yield return line;
        if (ctx.HasMore) {
            var nextSkip = ctx.EffectiveSkip + ctx.ShownLines;
            yield return $"\n... [已截断，共 {ctx.TotalLines} 行，显示第 {ctx.EffectiveSkip + 1}-{ctx.EffectiveSkip + ctx.ShownLines} 行。用 --skip_lines {nextSkip} 或 --start_line {nextSkip + 1} 续读后续内容]";
        }
    }
}
