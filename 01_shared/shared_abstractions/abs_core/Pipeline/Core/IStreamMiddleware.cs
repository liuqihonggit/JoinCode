namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// Stream 中间件 — 流式事件模式，适用于聊天、流式处理
/// 执行顺序由 StreamPipelineBuilder.Use() 注册顺序决定，无需 Order 属性
/// </summary>
public interface IStreamMiddleware<TContext, TEvent>
{
    /// <summary>
    /// 异常处理策略
    /// Stream 中间件默认 Propagate，因为流式场景异常传播更安全
    /// </summary>
    ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 处理流式事件，可在此前后注入自定义事件
    /// </summary>
    IAsyncEnumerable<TEvent> InvokeAsync(
        TContext context,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        CancellationToken ct);
}
