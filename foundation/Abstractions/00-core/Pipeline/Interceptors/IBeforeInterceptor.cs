namespace JoinCode.Abstractions.Pipeline.Interceptors;

/// <summary>
/// 前拦截器 — 在被拦截方法执行前调用。
/// 返回 false 短路（不调用原方法，直接返回 InterceptContext.ReturnValue）；
/// 返回 true 继续执行原方法。
/// </summary>
public interface IBeforeInterceptor
{
    /// <summary>前拦截逻辑 — false 短路，true 继续</summary>
    Task<bool> OnBeforeAsync(InterceptContext context, CancellationToken cancellationToken);
}
