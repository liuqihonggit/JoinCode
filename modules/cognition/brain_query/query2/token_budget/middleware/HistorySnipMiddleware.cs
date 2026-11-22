namespace Core.Query;

/// <summary>
/// 历史裁剪中间件 — 每次工具调用后检查是否需要裁剪对话历史（ADR 0156）
/// <para>不依赖外部 TokenBudgetManager 注入，内部直接估算 token 占用。</para>
/// <para>剩余预算 &lt; 15% 时触发裁剪，裁剪目标 65%。</para>
/// </summary>
[Register(typeof(IQueryMiddleware), ServiceLifetime.Singleton)]
public sealed partial class HistorySnipMiddleware : ServiceEntity, IQueryMiddleware {
    /// <summary>
    /// 构造函数 — 注入历史裁剪服务（可选）
    /// </summary>
    /// <param name="historySnipService">历史裁剪服务</param>
    /// <param name="tokenBudgetManager">Token 预算管理器（可选，保留兼容但不再必需）</param>
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
        if (_historySnipService is not null) {
            context.AfterToolCallHooks.Add(CheckAndSnipAsync);
        }

        return next(context, ct);
    }

    private async Task CheckAndSnipAsync(QueryMiddlewareContext context, CancellationToken ct) {
        var snipService = _historySnipService ?? throw new InvalidOperationException("History snip service not available.");
        var maxTokens = context.Config.MaxTokens;

        var remaining = _tokenBudgetManager is not null
            ? await _tokenBudgetManager.GetRemainingBudgetAsync(ct).ConfigureAwait(false)
            : EstimateRemainingBudget(context, maxTokens);

        if (remaining < maxTokens * 0.15) {
            var snipTarget = (int)(maxTokens * 0.65);
            var snipResult = await snipService.SnipByTokenLimitAsync(context.ChatHistory, snipTarget, ct).ConfigureAwait(false);
            context.Logger?.LogInformation("[QueryEngine] 历史裁剪完成: 移除 {Removed} 条消息", snipResult.MessagesRemoved);

            var postSnipRemaining = _tokenBudgetManager is not null
                ? await _tokenBudgetManager.GetRemainingBudgetAsync(ct).ConfigureAwait(false)
                : EstimateRemainingBudget(context, maxTokens);
            if (postSnipRemaining < maxTokens * 0.3) {
                context.Logger?.LogWarning("[QueryEngine] 历史裁剪后剩余预算仍不足（{Remaining} token），下一轮将触发预防性折叠", postSnipRemaining);
            }
        }
    }

    /// <summary>内部估算剩余 token 预算 — 不依赖外部 TokenBudgetManager</summary>
    private static long EstimateRemainingBudget(QueryMiddlewareContext context, int maxTokens) {
        var messages = context.ChatHistory;
        if (messages.Count == 0) {
            return maxTokens;
        }

        var totalChars = 0;
        foreach (var msg in messages) {
            totalChars += msg.Content?.Length ?? 0;
        }

        var estimatedTokens = totalChars / 4 + messages.Count * 4;
        return Math.Max(0, maxTokens - estimatedTokens);
    }
}