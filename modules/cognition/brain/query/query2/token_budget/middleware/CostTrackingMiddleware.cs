namespace Core.Query;

/// <summary>
/// 成本追踪中间件 — 每次 LLM 调用后追踪 Token 使用量和成本
/// </summary>
[Register(typeof(IQueryMiddleware), ServiceLifetime.Singleton)]
public sealed partial class CostTrackingMiddleware : ServiceEntity, IQueryMiddleware {
    private readonly ITokenCostTracker _costTracker;
    private readonly ICostTracker? _sessionCostTracker;
    private readonly IUsdBudgetManager? _usdBudgetManager;


    /// <summary>
    /// 错误处理策略 — 继续执行
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 构造函数 — 注入 Token 成本追踪器（可选，缺省使用空追踪器）
    /// TASK071 query2-② — ICostTracker（真实模型计价权威）优先；其缺失时回退 query2 内部估算器。
    /// </summary>
    /// <param name="costTracker">Token 成本追踪器（可选）</param>
    /// <param name="sessionCostTracker">会话成本追踪器（真实计价 — TASK071 query2-② 接线）</param>
    /// <param name="usdBudgetManager">USD 预算管理器（TASK071 query2-① — 记录增量驱动预算熔断）</param>
    public CostTrackingMiddleware(
        ITokenCostTracker? costTracker = null,
        ICostTracker? sessionCostTracker = null,
        IUsdBudgetManager? usdBudgetManager = null) {
        _costTracker = costTracker ?? new NullTokenCostTracker();
        _sessionCostTracker = sessionCostTracker;
        _usdBudgetManager = usdBudgetManager;
    }

    /// <summary>
    /// 注册 LLM 调用后钩子追踪成本
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct) {
        context.AfterLlmCallHooks.Add(TrackCostAsync);
        return next(context, ct);
    }

    private async Task TrackCostAsync(QueryMiddlewareContext context, CancellationToken ct) {
        _costTracker.TrackUsage(context.InputTokens, context.OutputTokens);

        var totalTokens = context.InputTokens + context.OutputTokens;
        if (totalTokens > 0) {
            context.RecentConsumptions.Add(new TokenConsumption {
                Amount = totalTokens,
                Reason = "LLM调用",
                ToolName = context.ToolName
            });
        }

        // TASK071 query2-② — 真实模型计价优先（ICostTracker.GetTotalStatistics 权威口径），
        // 缺失时回退 query2 内部估算；旧实现恒走 NullTracker → Complete chunk 的 CostUsd 恒 0
        var totalCost = _sessionCostTracker?.GetTotalStatistics().TotalCostUsd ?? _costTracker.GetTotalCost();

        // TASK071 query2-① — 对齐 TS QueryEngine.ts:970-972（每条消息后 getTotalCost 对照 maxBudgetUsd）：
        // 增量记录到预算管理器（Actor 累计），驱动 IsBudgetExceededAsync 熔断
        var delta = totalCost - context.TotalCostUsd;
        if (_usdBudgetManager is not null && delta > 0) {
            await _usdBudgetManager.RecordCostAsync(delta, $"LLM调用: {context.ToolName ?? "main"}", ct).ConfigureAwait(false);
        }

        context.TotalCostUsd = totalCost;
    }
}