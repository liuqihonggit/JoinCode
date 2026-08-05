namespace JoinCode.Abstractions.Pipeline.Interceptors;

/// <summary>
/// 拦截器上下文 — 传递给 IBeforeInterceptor/IAfterInterceptor 的调用信息。
/// 前拦截器可读入参并短路；后拦截器可读/改返回值。
/// </summary>
public sealed class InterceptContext
{
    /// <summary>被拦截的方法名</summary>
    public required string MethodName { get; init; }

    /// <summary>被拦截的目标对象（装饰器的 inner 实例）</summary>
    public object? Target { get; init; }

    /// <summary>方法入参（按声明顺序）</summary>
    public IReadOnlyList<object?> Arguments { get; init; } = Array.Empty<object?>();

    /// <summary>方法返回值 — 前拦截器短路时为 null，后拦截器可读取/修改</summary>
    public object? ReturnValue { get; set; }

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }
}
