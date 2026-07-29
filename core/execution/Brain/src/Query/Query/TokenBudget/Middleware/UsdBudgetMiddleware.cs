using JoinCode.Abstractions.Attributes;

namespace Core.Query;

/// <summary>
/// USD 预算中间件 — 每次迭代前检查 USD 预算是否超限
/// </summary>
[Register(typeof(IQueryMiddleware))]
public sealed partial class UsdBudgetMiddleware : IQueryMiddleware
{
    [Inject] private readonly IUsdBudgetManager? _usdBudgetManager;


    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册迭代前钩子检查 USD 预算
    /// </summary>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct)
    {
        if (_usdBudgetManager is not null)
        {
            context.BeforeIterationHooks.Add(CheckBudgetAsync);
        }

        return next(context, ct);
    }

    private async Task CheckBudgetAsync(QueryMiddlewareContext context, CancellationToken ct)
    {
        var usdBudgetManager = _usdBudgetManager ?? throw new InvalidOperationException("UsdBudgetManager not available.");
        if (await usdBudgetManager.IsBudgetExceededAsync(ct).ConfigureAwait(false))
        {
            context.Logger?.LogWarning("[QueryEngine] USD 预算已超限");
            context.OutputChunks.Add(new QueryStreamChunk { Type = AgentStreamChunkType.Error, Content = "USD 预算已超限" });
            context.ShouldStop = true;
        }
    }
}
