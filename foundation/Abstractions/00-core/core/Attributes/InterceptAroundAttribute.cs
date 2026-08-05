namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记方法由源码生成器自动织入前/后拦截器（AOP 环绕通知）— 生成器据此生成装饰器类，在 DI 注册时自动包装。
/// 适用于技术横切（权限、日志、遥测、审计），非业务事件（业务事件用 IHookOrchestrator 显式 Raise）。
/// 与 <see cref="InterceptAttribute"/>（异常拦截）语义不同：本特性织入 IBeforeInterceptor/IAfterInterceptor 对。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class InterceptAroundAttribute : Attribute
{
    /// <summary>前拦截器类型，须实现 IBeforeInterceptor；null 表示无前拦截</summary>
    public Type? Before { get; }

    /// <summary>后拦截器类型，须实现 IAfterInterceptor；null 表示无后拦截</summary>
    public Type? After { get; }

    /// <param name="before">前拦截器类型，须实现 IBeforeInterceptor</param>
    /// <param name="after">后拦截器类型，须实现 IAfterInterceptor</param>
    public InterceptAroundAttribute(Type? before = null, Type? after = null)
    {
        Before = before;
        After = after;
    }
}
