namespace McpToolDispatch;

/// <summary>
/// 日志处理管道步骤 — 中间件洋葱模型,每步独立可替换可测试
/// <para>串联: Download → Filter → Pagination → LineNumber → PageHeader → ContinueHint</para>
/// <para>改分页只改 PaginationStep,改页眉只改 PageHeaderStep,互不影响</para>
/// </summary>
internal interface ILogPipelineStep {
    /// <summary>处理输入流,返回处理后的流</summary>
    IAsyncEnumerable<string> ProcessAsync(LogContext ctx, IAsyncEnumerable<string> input, CancellationToken ct);
}
