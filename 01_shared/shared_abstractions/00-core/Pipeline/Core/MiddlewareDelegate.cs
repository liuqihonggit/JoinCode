namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 中间件委托 — 调用下一个中间件或终端处理器
/// </summary>
public delegate Task MiddlewareDelegate<TContext>(TContext context, CancellationToken ct);
