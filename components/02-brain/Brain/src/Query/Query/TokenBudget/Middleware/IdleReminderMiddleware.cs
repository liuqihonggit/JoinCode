using JoinCode.Abstractions.Attributes;

namespace Core.Query;

/// <summary>
/// 空闲提醒中间件 — 每次迭代后记录助手轮次
/// </summary>
[Register(typeof(IQueryMiddleware))]
public sealed partial class IdleReminderMiddleware : IQueryMiddleware
{
    [Inject] private readonly IToolIdleReminderService? _toolIdleReminder;


    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册工具调用后钩子和查询完成钩子记录助手轮次
    /// </summary>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct)
    {
        if (_toolIdleReminder is not null)
        {
            context.AfterToolCallHooks.Add(RecordToolCallTurnAsync);
            context.OnCompleteHooks.Add(RecordCompletionTurnAsync);
        }

        return next(context, ct);
    }

    private Task RecordToolCallTurnAsync(QueryMiddlewareContext context, CancellationToken ct)
    {
        _toolIdleReminder!.RecordAssistantTurn(context.ToolName);
        return Task.CompletedTask;
    }

    private Task RecordCompletionTurnAsync(QueryMiddlewareContext context, CancellationToken ct)
    {
        _toolIdleReminder!.RecordAssistantTurn();
        return Task.CompletedTask;
    }
}
