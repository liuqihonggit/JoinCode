namespace Core.Query;

/// <summary>
/// USD 预算中间件 — 每次迭代前检查 USD 预算是否超限
/// </summary>
[Register(typeof(IQueryMiddleware), ServiceLifetime.Singleton)]
public sealed partial class UsdBudgetMiddleware : ServiceEntity, IQueryMiddleware
{
    /// <summary>
    /// 构造函数 — 注入 USD 预算管理器（可选）
    /// </summary>
    /// <param name="usdBudgetManager">USD 预算管理器</param>
    public UsdBudgetMiddleware(IUsdBudgetManager? usdBudgetManager = null)
    {
        _usdBudgetManager = usdBudgetManager;
    }
    private readonly IUsdBudgetManager? _usdBudgetManager;


    /// <summary>
    /// 错误处理策略 — 继续执行
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册迭代前钩子检查 USD 预算
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
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
