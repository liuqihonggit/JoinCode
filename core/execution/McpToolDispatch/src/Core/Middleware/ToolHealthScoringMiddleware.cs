namespace McpToolRegistry;

/// <summary>
/// 工具健康评分中间件 — Order=850 — 执行后根据结果更新评分，熔断工具短路返回
/// </summary>
[Register]
public sealed partial class ToolHealthScoringMiddleware : IToolExecutionMiddleware
{
    [Inject] private readonly ToolHealthMonitor _monitor = null!;
    [Inject] private readonly ILogger<ToolHealthScoringMiddleware> _logger = null!;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct)
    {
        var record = await _monitor.GetRecordAsync(context.ToolName, ct).ConfigureAwait(false);

        if (record is not null && !record.IsEnabled)
        {
            _logger.LogWarning("工具 {ToolName} 已熔断禁用（连续失败{Count}次，评分{Score}）",
                context.ToolName, record.ConsecutiveFailures, record.Score);
            context.Result = new ToolResult
            {
                Content = [new ToolContent
                {
                    Type = ToolContentType.Text,
                    Text = $"工具 '{context.ToolName}' 已被熔断禁用（连续失败{record.ConsecutiveFailures}次）。" +
                           $"评分: {record.Score}。请尝试替代工具或使用 /tools reset {context.ToolName} 重置。"
                }],
                IsError = true
            };
            return;
        }

        await next(context, ct).ConfigureAwait(false);

        if (context.Result is null) return;

        if (context.Result.IsError)
        {
            var errorMsg = context.Result.GetFirstText();
            await _monitor.RecordFailureAsync(context.ToolName, errorMsg, ct).ConfigureAwait(false);
        }
        else
        {
            await _monitor.RecordSuccessAsync(context.ToolName, ct).ConfigureAwait(false);
        }
    }
}
