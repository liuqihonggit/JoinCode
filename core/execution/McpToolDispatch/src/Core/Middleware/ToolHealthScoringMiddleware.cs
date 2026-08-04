namespace McpToolRegistry;

/// <summary>
/// 工具健康评分中间件 — Order=850 — 执行后根据结果更新评分，熔断工具短路返回
/// 支持黑名单（完全禁用）、降权（额外扣分）、超图评分（关联工具共享评分空间）
/// </summary>
[Register]
public sealed partial class ToolHealthScoringMiddleware : IToolExecutionMiddleware
{
    [Inject] private readonly ToolHealthMonitor _monitor = null!;
    [Inject] private readonly ToolHypergraphScorer _scorer = null!;
    [Inject] private readonly ILogger<ToolHealthScoringMiddleware> _logger = null!;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct)
    {
        if (_monitor.IsBlacklisted(context.ToolName))
        {
            _logger.LogWarning("工具 {ToolName} 已被用户加入黑名单，拒绝执行", context.ToolName);
            context.Result = new ToolResult
            {
                Content = [new ToolContent
                {
                    Type = ToolContentType.Text,
                    Text = $"工具 '{context.ToolName}' 已被用户禁用（黑名单）。" +
                           $"如需重新启用，请在 settings.json 的 toolExecution.blacklistedTools 中移除。"
                }],
                IsError = true
            };
            return;
        }

        var record = await _monitor.GetRecordAsync(context.ToolName, ct).ConfigureAwait(false);

        if (record is not null && !record.IsEnabled)
        {
            var effectiveScore = _scorer.CalculateFinalScore(context.ToolName, record.Score);
            _logger.LogWarning("工具 {ToolName} 已熔断禁用（连续失败{Count}次，独立评分{Score}，超图评分{EffectiveScore}）",
                context.ToolName, record.ConsecutiveFailures, record.Score, effectiveScore);
            context.Result = new ToolResult
            {
                Content = [new ToolContent
                {
                    Type = ToolContentType.Text,
                    Text = $"工具 '{context.ToolName}' 已被熔断禁用（连续失败{record.ConsecutiveFailures}次）。" +
                           $"评分: {effectiveScore}。请尝试替代工具或使用 /tools reset {context.ToolName} 重置。"
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

        // 执行后更新超边共享评分
        var allRecords = await _monitor.GetAllRecordsAsync(ct).ConfigureAwait(false);
        _scorer.UpdateSharedScores(allRecords);
    }
}
