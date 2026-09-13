namespace Core.Query;

/// <summary>
/// 成本追踪中间件 — 每次 LLM 调用后追踪 Token 使用量和成本
/// </summary>
[Register(typeof(IQueryMiddleware), ServiceLifetime.Singleton)]
public sealed partial class CostTrackingMiddleware : ServiceEntity, IQueryMiddleware
{
    private readonly ITokenCostTracker _costTracker;


    /// <summary>
    /// 错误处理策略 — 继续执行
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 构造函数 — 注入 Token 成本追踪器（可选，缺省使用空追踪器）
    /// </summary>
    /// <param name="costTracker">Token 成本追踪器</param>
    public CostTrackingMiddleware(ITokenCostTracker? costTracker = null)
    {
        _costTracker = costTracker ?? new NullTokenCostTracker();
    }

    /// <summary>
    /// 注册 LLM 调用后钩子追踪成本
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct)
    {
        context.AfterLlmCallHooks.Add(TrackCostAsync);
        return next(context, ct);
    }

    private Task TrackCostAsync(QueryMiddlewareContext context, CancellationToken ct)
    {
        _costTracker.TrackUsage(context.InputTokens, context.OutputTokens);

        var totalTokens = context.InputTokens + context.OutputTokens;
        if (totalTokens > 0)
        {
            context.RecentConsumptions.Add(new TokenConsumption
            {
                Amount = totalTokens,
                Reason = "LLM调用",
                ToolName = context.ToolName
            });
        }

        context.TotalCostUsd = _costTracker.GetTotalCost();
        return Task.CompletedTask;
    }
}
