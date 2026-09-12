namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 事件分发模式 — 对齐 DSH Cordis 五种 dispatch
/// <para>Emit：同步触发不等监听器（fire-and-forget）</para>
/// <para>Parallel：异步并行 await 全部</para>
/// <para>Serial：串行依次 await</para>
/// <para>Bail：首个 bail（返回 true）即停</para>
/// <para>Waterfall：监听器连成 next() 链，next() 返回值递下一个</para>
/// </summary>
public enum EventDispatchMode
{
    /// <summary>同步触发不等监听器</summary>
    Emit,

    /// <summary>异步并行 await 全部</summary>
    Parallel,

    /// <summary>串行依次 await</summary>
    Serial,

    /// <summary>首个 bail 结果即停</summary>
    Bail,

    /// <summary>next() 链，漏调 next() 短路</summary>
    Waterfall,
}

/// <summary>
/// 事件分发器 — 5 种策略实现，对齐 DSH ctx.emit/parallel/serial/bail/waterfall
/// <para>纯静态工具类，无状态，AOT 兼容</para>
/// </summary>
public static class EventDispatcher
{
    /// <summary>
    /// Emit：同步触发不等监听器（fire-and-forget）
    /// <para>对齐 DSH emit：不 await，异常吞掉（调用方自行处理）</para>
    /// </summary>
    public static Task EmitAsync<T>(
        IReadOnlyList<Func<T, CancellationToken, Task>> handlers,
        T arg,
        CancellationToken ct)
    {
        for (int i = 0; i < handlers.Count; i++)
        {
            _ = handlers[i](arg, ct);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Parallel：异步并行 await 全部
    /// <para>对齐 DSH parallel：Task.WhenAll</para>
    /// </summary>
    public static async Task ParallelAsync<T>(
        IReadOnlyList<Func<T, CancellationToken, Task>> handlers,
        T arg,
        CancellationToken ct)
    {
        if (handlers.Count == 0) return;
        var tasks = new Task[handlers.Count];
        for (int i = 0; i < handlers.Count; i++)
        {
            tasks[i] = handlers[i](arg, ct);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Serial：串行依次 await
    /// <para>对齐 DSH serial：顺序执行，前一个完成才下一个</para>
    /// </summary>
    public static async Task SerialAsync<T>(
        IReadOnlyList<Func<T, CancellationToken, Task>> handlers,
        T arg,
        CancellationToken ct)
    {
        for (int i = 0; i < handlers.Count; i++)
        {
            await handlers[i](arg, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Bail：首个 bail（返回 true）即停
    /// <para>对齐 DSH bail：返回 true 表示已处理，停止后续监听器</para>
    /// <returns>true 若有监听器 bail，false 若全部放行</returns>
    /// </summary>
    public static async Task<bool> BailAsync<T>(
        IReadOnlyList<Func<T, CancellationToken, Task<bool>>> handlers,
        T arg,
        CancellationToken ct)
    {
        for (int i = 0; i < handlers.Count; i++)
        {
            if (await handlers[i](arg, ct).ConfigureAwait(false))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Waterfall：next() 链，监听器依次处理，next() 传递给下一个
    /// <para>对齐 DSH waterfall：漏调 next() 短路整条链，返回 initial</para>
    /// <para>从后往前构建链：最后一个 handler 的 next() 返回 initial</para>
    /// <para>handler 签名：(arg, next, ct) => result，next() 调用下一个监听器</para>
    /// </summary>
    public static async Task<T> WaterfallAsync<T>(
        IReadOnlyList<Func<T, Func<CancellationToken, Task<T>>, CancellationToken, Task<T>>> handlers,
        T initial,
        CancellationToken ct)
    {
        if (handlers.Count == 0) return initial;

        Func<CancellationToken, Task<T>> next = _ => Task.FromResult(initial);
        for (int i = handlers.Count - 1; i >= 0; i--)
        {
            var handler = handlers[i];
            var innerNext = next;
            next = token => handler(initial, innerNext, token);
        }
        return await next(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 按模式分发（Emit/Parallel/Serial），Bail/Waterfall 需专用方法
    /// </summary>
    public static Task DispatchAsync<T>(
        EventDispatchMode mode,
        IReadOnlyList<Func<T, CancellationToken, Task>> handlers,
        T arg,
        CancellationToken ct)
    {
        return mode switch
        {
            EventDispatchMode.Emit => EmitAsync(handlers, arg, ct),
            EventDispatchMode.Parallel => ParallelAsync(handlers, arg, ct),
            EventDispatchMode.Serial => SerialAsync(handlers, arg, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "[INF-EVENT-DISPATCH] Bail/Waterfall 需专用方法，不支持通用 DispatchAsync"),
        };
    }
}
