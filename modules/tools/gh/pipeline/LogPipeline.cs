namespace McpToolDispatch;

/// <summary>
/// 日志处理管道 — 串联多个 ILogPipelineStep,洋葱模型
/// <para>source → Step1 → Step2 → ... → 输出</para>
/// <para>每步独立,改一处不影响其他,符合 AGENTS.md 中间件洋葱模型规范</para>
/// </summary>
internal sealed class LogPipeline(ILogPipelineStep[] steps) {
    /// <summary>串联多个 Step 运行管道: source → Step1 → Step2 → ... → 输出</summary>
    public async IAsyncEnumerable<string> RunAsync(LogContext ctx, IAsyncEnumerable<string> source,
        [EnumeratorCancellation] CancellationToken ct) {
        var current = source;
        foreach (var step in steps) {
            current = step.ProcessAsync(ctx, current, ct);
        }
        await foreach (var line in current.ConfigureAwait(false)) {
            yield return line;
        }
    }

    /// <summary>创建默认格式化管道: Pagination → LineNumber → ContinueHint</summary>
    public static LogPipeline CreateFormatPipeline() {
        return new LogPipeline([
            new PaginationStep(),
            new LineNumberStep(),
            new ContinueHintStep(),
        ]);
    }
}
