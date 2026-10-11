namespace Core.Query;

/// <summary>
/// 历史裁剪中间件 — 每次工具调用后检查是否需要裁剪对话历史
/// </summary>
[Register(typeof(IQueryMiddleware), ServiceLifetime.Singleton)]
public sealed partial class HistorySnipMiddleware : ServiceEntity, IQueryMiddleware {
    /// <summary>
    /// 构造函数 — 注入历史裁剪服务和 Token 预算管理器（均可选）
    /// </summary>
    /// <param name="historySnipService">历史裁剪服务</param>
    /// <param name="tokenBudgetManager">Token 预算管理器</param>
    public HistorySnipMiddleware(IHistorySnipService? historySnipService = null, ITokenBudgetManager? tokenBudgetManager = null) {
        _historySnipService = historySnipService;
        _tokenBudgetManager = tokenBudgetManager;
    }
    private readonly IHistorySnipService? _historySnipService;
    private readonly ITokenBudgetManager? _tokenBudgetManager;


    /// <summary>
    /// 错误处理策略 — 继续执行
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册工具调用后钩子检查历史裁剪
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct) {
        if (_historySnipService is not null && _tokenBudgetManager is not null) {
            context.AfterToolCallHooks.Add(CheckAndSnipAsync);
        }

        return next(context, ct);
    }

    private async Task CheckAndSnipAsync(QueryMiddlewareContext context, CancellationToken ct) {
        var budgetManager = _tokenBudgetManager ?? throw new InvalidOperationException("Token budget manager not available.");
        var snipService = _historySnipService ?? throw new InvalidOperationException("History snip service not available.");
        var remaining = await budgetManager.GetRemainingBudgetAsync(ct).ConfigureAwait(false);
        if (remaining < context.Config.MaxTokens * 0.1) {
            var snipTarget = (int)(context.Config.MaxTokens * 0.7);
            var snipResult = await snipService.SnipByTokenLimitAsync(context.ChatHistory, snipTarget, ct).ConfigureAwait(false);
            context.Logger?.LogInformation("[QueryEngine] 历史裁剪完成: 移除 {Removed} 条消息", snipResult.MessagesRemoved);
        }
    }
}