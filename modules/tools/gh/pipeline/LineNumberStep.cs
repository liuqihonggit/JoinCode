namespace McpToolDispatch;

/// <summary>
/// 行号步骤 — 为每行加行号前缀(tab/箭头),单行不加保持简洁
/// <para>行号从 EffectiveSkip+1 开始(续读时行号连续)</para>
/// </summary>
internal sealed class LineNumberStep : ILogPipelineStep {
    /// <summary>为每行加行号前缀(tab/箭头),单行不加保持简洁</summary>
    public async IAsyncEnumerable<string> ProcessAsync(LogContext ctx, IAsyncEnumerable<string> input,
        [EnumeratorCancellation] CancellationToken ct) {
        if (!ctx.AddLineNumbers) {
            await foreach (var line in input.ConfigureAwait(false)) yield return line;
            yield break;
        }
        var lineNum = ctx.EffectiveSkip;
        var first = true;
        await foreach (var line in input.ConfigureAwait(false)) {
            ct.ThrowIfCancellationRequested();
            if (first && ctx.ShownLines <= 1) { yield return line; first = false; continue; }
            first = false;
            yield return LineNumberFormatter.Format(++lineNum, line, ctx.CompactLinePrefix);
        }
    }
}
