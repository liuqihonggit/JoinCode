using JoinCode.Abstractions.Attributes;

namespace Core.Query;

/// <summary>
/// 状态转换中间件 — 查询开始前和完成后转换查询状态
/// </summary>
[Register(typeof(IQueryMiddleware))]
public sealed partial class StateTransitionMiddleware : IQueryMiddleware
{
    [Inject] private readonly IQueryStateTransitions? _stateTransitions;


    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 查询开始前转换到 Initializing → Running，完成后转换到 Completed
    /// </summary>
    public async Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct)
    {
        if (_stateTransitions is not null)
        {
            _stateTransitions.TransitionTo(QueryState.Initializing);
            _stateTransitions.TransitionTo(QueryState.Running);
        }

        await next(context, ct).ConfigureAwait(false);

        if (_stateTransitions is not null && context.IsQueryComplete)
        {
            _stateTransitions.TransitionTo(QueryState.Completed);
        }
    }
}
