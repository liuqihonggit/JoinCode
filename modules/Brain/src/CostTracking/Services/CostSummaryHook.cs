namespace Core.CostTracking;

public interface ICostSummaryHook
{
    Task<string> GenerateSummaryAsync(CancellationToken ct = default);
    Task PrintSummaryOnExitAsync(CancellationToken ct = default);
}

[Register]
public sealed partial class CostSummaryHook : ICostSummaryHook
{
    [Inject] private readonly CostTracker _costTracker;
    [Inject] private readonly ILogger<CostSummaryHook>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;

    public Task<string> GenerateSummaryAsync(CancellationToken ct = default)
    {
        var totalStats = _costTracker.GetTotalStatistics();
        var todayStats = _costTracker.GetTodayStatistics();

        var sb = new StringBuilder();
        sb.AppendLine("===== 成本摘要 =====");
        sb.AppendLine();
        sb.AppendLine($"总会话统计:");
        sb.AppendLine($"  请求次数: {totalStats.RequestCount}");
        sb.AppendLine($"  总Token数: {totalStats.TotalTokens:N0} (Prompt: {totalStats.PromptTokens:N0}, Completion: {totalStats.CompletionTokens:N0})");
        sb.AppendLine($"  总成本: ${totalStats.TotalCostUsd:F4}");
        sb.AppendLine();

        if (totalStats.CacheCreationTokens > 0 || totalStats.CacheReadTokens > 0)
        {
            sb.AppendLine($"  缓存创建Token: {totalStats.CacheCreationTokens:N0}");
            sb.AppendLine($"  缓存读取Token: {totalStats.CacheReadTokens:N0}");
            sb.AppendLine($"  缓存节省: ${totalStats.CacheSavingsUsd:F4}");
            sb.AppendLine();
        }

        sb.AppendLine($"今日统计:");
        sb.AppendLine($"  请求次数: {todayStats.RequestCount}");
        sb.AppendLine($"  总Token数: {todayStats.TotalTokens:N0}");
        sb.AppendLine($"  今日成本: ${todayStats.TotalCostUsd:F4}");
        sb.AppendLine();

        if (totalStats.ModelBreakdown.Count > 0)
        {
            sb.AppendLine("按模型分类:");
            foreach (var model in totalStats.ModelBreakdown)
            {
                sb.AppendLine($"  {model.Model}:");
                sb.AppendLine($"    请求: {model.RequestCount}, Token: {model.TotalTokens:N0}, 成本: ${model.TotalCost:F4}");
            }
        }

        return Task.FromResult(sb.ToString());
    }

    public async Task PrintSummaryOnExitAsync(CancellationToken ct = default)
    {
        var summary = await GenerateSummaryAsync(ct).ConfigureAwait(false);
        _logger?.LogInformation("{Summary}", summary);
        _telemetryService?.RecordCount("cost.summary.count", description: "Cost summary generation count");
    }
}
