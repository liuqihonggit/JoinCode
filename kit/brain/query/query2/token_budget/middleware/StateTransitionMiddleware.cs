namespace Core.Query;

/// <summary>
/// 状态转换中间件 — 查询开始前和完成后转换查询状态
/// </summary>
[Register(typeof(IQueryMiddleware), ServiceLifetime.Singleton)]
public sealed partial class StateTransitionMiddleware : ServiceEntity, IQueryMiddleware
{
    /// <summary>
    /// 构造函数 — 注入查询状态转换器（可选）
    /// </summary>
    /// <param name="stateTransitions">查询状态转换器</param>
    public StateTransitionMiddleware(IQueryStateTransitions? stateTransitions = null)
    {
        _stateTransitions = stateTransitions;
    }
    private readonly IQueryStateTransitions? _stateTransitions;


    /// <summary>
    /// 错误处理策略 — 继续执行
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 查询开始前转换到 Initializing → Running，完成后转换到 Completed
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct)
    {
        if (_stateTransitions is not null)
        {
            var current = _stateTransitions.CurrentState;
            if (current is QueryState.Completed or QueryState.Failed or QueryState.Cancelled or QueryState.Running)
            {
                _stateTransitions.Reset();
            }

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
