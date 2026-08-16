namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// Stream 中间件委托 — 调用下一个中间件或终端处理器
/// </summary>
public delegate IAsyncEnumerable<TEvent> StreamMiddlewareDelegate<TContext, TEvent>(
    TContext context, CancellationToken ct);
