using JoinCode.Abstractions.Attributes;

namespace Core.Query;

/// <summary>
/// 历史裁剪中间件 — 每次工具调用后检查是否需要裁剪对话历史
/// </summary>
[Register(typeof(IQueryMiddleware))]
public sealed partial class HistorySnipMiddleware : IQueryMiddleware
{
    [Inject] private readonly IHistorySnipService? _historySnipService;
    [Inject] private readonly ITokenBudgetManager? _tokenBudgetManager;


    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册工具调用后钩子检查历史裁剪
    /// </summary>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct)
    {
        if (_historySnipService is not null && _tokenBudgetManager is not null)
        {
            context.AfterToolCallHooks.Add(CheckAndSnipAsync);
        }

        return next(context, ct);
    }

    private async Task CheckAndSnipAsync(QueryMiddlewareContext context, CancellationToken ct)
    {
        var remaining = await _tokenBudgetManager!.GetRemainingBudgetAsync(ct).ConfigureAwait(false);
        if (remaining < context.Config.MaxTokens * 0.1)
        {
            var snipTarget = (int)(context.Config.MaxTokens * 0.7);
            var snipResult = await _historySnipService!.SnipByTokenLimitAsync(context.ChatHistory, snipTarget, ct).ConfigureAwait(false);
            context.Logger?.LogInformation("[QueryEngine] 历史裁剪完成: 移除 {Removed} 条消息", snipResult.MessagesRemoved);
        }
    }
}
