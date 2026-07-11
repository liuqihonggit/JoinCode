using JoinCode.Abstractions.Attributes;

namespace JoinCode.App.Middlewares;

/// <summary>
/// Token 预算中间件 — 发送前检查剩余预算，超限则短路返回提示
/// Order=60 在 PreviewChatMiddleware(50) 之后执行，确保 IsDryRun 已设置
/// 预览模式下跳过预算检查（预览不消耗 Token）
/// </summary>
[Register(typeof(Core.Context.IChatMiddleware))]
internal sealed partial class TokenBudgetMiddleware : Core.Context.IChatMiddleware
{
    private readonly JoinCode.Abstractions.Interfaces.ITokenBudgetManager _budgetManager;
    private readonly ILogger<TokenBudgetMiddleware> _logger;

    public TokenBudgetMiddleware(
        JoinCode.Abstractions.Interfaces.ITokenBudgetManager budgetManager,
        ILogger<TokenBudgetMiddleware> logger)
    {
        _budgetManager = budgetManager;
        _logger = logger;
    }

    public async IAsyncEnumerable<JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> InvokeAsync(
        Core.Context.ChatMiddlewareContext context,
        JoinCode.Abstractions.Pipeline.StreamMiddlewareDelegate<Core.Context.ChatMiddlewareContext, JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // 预览模式不消耗 Token，跳过预算检查
        if (context.IsDryRun)
        {
            await foreach (var evt in next(context, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
            yield break;
        }

        var remaining = await _budgetManager.GetRemainingBudgetAsync(ct).ConfigureAwait(false);
        Console.Error.WriteLine($"[TokenBudget] remaining={remaining}, IsDryRun={context.IsDryRun}");

        if (remaining <= 0)
        {
            _logger.LogWarning("[TokenBudget] 预算已耗尽，短路返回");
            Console.Error.WriteLine("[TokenBudget] SHORT-CIRCUIT: 预算已耗尽");
            yield return JoinCode.Abstractions.LLM.Chat.ChatStreamEvent.Text("[Token 预算已耗尽] 本轮对话已跳过，请重置预算后重试。");
            yield break;
        }

        Console.Error.WriteLine("[TokenBudget] calling next...");
        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}
