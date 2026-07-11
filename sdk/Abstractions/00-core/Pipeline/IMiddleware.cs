namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// Task 中间件 — 请求-响应模式，适用于管理操作、初始化、压缩等
/// 框架自动处理异常，中间件实现不需要 try-catch
/// 执行顺序由 PipelineBuilder.Use() 注册顺序决定，无需 Order 属性
/// </summary>
public interface IMiddleware<TContext>
{
    /// <summary>
    /// 异常处理策略
    /// Continue: 捕获异常，调用 onError，继续下一个中间件
    /// Propagate: 传播异常，中断管道
    /// </summary>
    ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 处理请求，可在此前后注入自定义逻辑
    /// </summary>
    Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct);
}
