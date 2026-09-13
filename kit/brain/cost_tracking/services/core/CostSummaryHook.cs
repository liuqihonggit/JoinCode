namespace Core.CostTracking;

/// <summary>
/// 成本摘要钩子接口 — 生成成本摘要并在退出时打印
/// </summary>
public interface ICostSummaryHook
{
    /// <summary>
    /// 异步生成成本摘要文本
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>成本摘要文本</returns>
    Task<string> GenerateSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// 异步在程序退出时打印成本摘要
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task PrintSummaryOnExitAsync(CancellationToken ct = default);
}

/// <summary>
/// 成本摘要钩子 — 汇总会话总成本与今日成本，按模型分类输出摘要
/// </summary>
[Register(typeof(ICostSummaryHook), ServiceLifetime.Singleton)]
public sealed partial class CostSummaryHook : ServiceEntity, ICostSummaryHook
{

    /// <summary>
    /// 构造成本摘要钩子实例
    /// </summary>
    /// <param name="costTracker">成本跟踪器</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="telemetryService">遥测服务（可选）</param>
    public CostSummaryHook(CostTracker costTracker, ILogger<CostSummaryHook>? logger = null, ITelemetryService? telemetryService = null)
    {
        _costTracker = costTracker;
        _logger = logger;
        _telemetryService = telemetryService;
    }
    private readonly CostTracker _costTracker;
    private readonly ILogger<CostSummaryHook>? _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 异步生成成本摘要文本
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>成本摘要文本</returns>
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

    /// <summary>
    /// 异步在程序退出时打印成本摘要
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task PrintSummaryOnExitAsync(CancellationToken ct = default)
    {
        var summary = await GenerateSummaryAsync(ct).ConfigureAwait(false);
        _logger?.LogInformation("{Summary}", summary);
        _telemetryService?.RecordCount("cost.summary.count", description: "Cost summary generation count");
    }
}
