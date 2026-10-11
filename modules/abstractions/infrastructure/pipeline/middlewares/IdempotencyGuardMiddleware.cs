namespace Infrastructure.Pipeline.Middlewares;

/// <summary>
/// 幂等守卫中间件 — 键控去重，命中缓存则短路管道，未命中则执行管道并缓存结果。
/// <para><b>工作流</b>:提取key → 命中缓存→设置Result短路 | 未命中→执行next→缓存Result</para>
/// <para><b>位置</b>:应注册在管道最前面,在业务中间件之前检查去重。</para>
/// <para><b>异常策略</b>:Propagate — 幂等守卫是关键操作,异常直接传播中断管道。</para>
/// </summary>
/// <typeparam name="TContext">管道上下文类型,需实现 IIdempotentContext&lt;TResult&gt;</typeparam>
/// <typeparam name="TResult">操作结果类型</typeparam>
/// <param name="_store">幂等去重存储</param>
public sealed class IdempotencyGuardMiddleware<TContext, TResult>(
    IIdempotencyStore _store) : IMiddleware<TContext>
    where TContext : IIdempotentContext<TResult> {

    /// <summary>异常策略:传播异常,幂等守卫是关键操作</summary>
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 处理请求 — 命中缓存则短路,未命中则执行管道并缓存结果。
    /// <para>key 为 None 时透传(无幂等控制);非空 key 命中缓存时设置 Result 并短路;未命中时执行 next 后缓存 Result。</para>
    /// </summary>
    /// <param name="context">管道上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct) {
        var key = context.IdempotencyKey;
        if (key.IsEmpty) {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        if (_store.TryGetResult<TResult>(key, out var cached)) {
            context.Result = cached;
            return;
        }

        await next(context, ct).ConfigureAwait(false);
        _store.TryRegister(key, context.Result);
    }
}
