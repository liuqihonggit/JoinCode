namespace JoinCode.Abstractions.Pipeline.Interceptors;

/// <summary>
/// 后拦截器 — 在被拦截方法执行后调用。
/// 可读取/修改 InterceptContext.ReturnValue（修改后的值将作为最终返回值）。
/// </summary>
public interface IAfterInterceptor
{
    /// <summary>后拦截逻辑 — 可读/改 ReturnValue</summary>
    Task OnAfterAsync(InterceptContext context, CancellationToken cancellationToken);
}
