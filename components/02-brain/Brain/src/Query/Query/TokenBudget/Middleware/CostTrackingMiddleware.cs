using JoinCode.Abstractions.Attributes;

namespace Core.Query;

/// <summary>
/// 成本追踪中间件 — 每次 LLM 调用后追踪 Token 使用量和成本
/// </summary>
[Register(typeof(IQueryMiddleware))]
public sealed partial class CostTrackingMiddleware : IQueryMiddleware
{
    private readonly ITokenCostTracker _costTracker;


    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public CostTrackingMiddleware(ITokenCostTracker? costTracker = null)
    {
        _costTracker = costTracker ?? new NullTokenCostTracker();
    }

    /// <summary>
    /// 注册 LLM 调用后钩子追踪成本
    /// </summary>
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
