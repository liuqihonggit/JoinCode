using JoinCode.Abstractions.Attributes;

namespace Core.Query;

/// <summary>
/// Token 预算中间件 — 每次 LLM 调用后消耗 Token 预算
/// </summary>
[Register(typeof(IQueryMiddleware))]
public sealed partial class TokenBudgetMiddleware : IQueryMiddleware
{
    [Inject] private readonly ITokenBudgetManager? _tokenBudgetManager;


    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册 LLM 调用后钩子消耗 Token 预算
    /// </summary>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct)
    {
        if (_tokenBudgetManager is not null)
        {
            context.AfterLlmCallHooks.Add(ConsumeTokensAsync);
        }

        return next(context, ct);
    }

    private async Task ConsumeTokensAsync(QueryMiddlewareContext context, CancellationToken ct)
    {
        var totalTokens = context.InputTokens + context.OutputTokens;
        if (totalTokens > 0)
        {
            await _tokenBudgetManager!.ConsumeTokensAsync(totalTokens, "LLM调用", context.ToolName, ct).ConfigureAwait(false);
        }
    }
}
