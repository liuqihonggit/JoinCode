namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 服务可调用标记 — 标记服务的主调用方法
/// <para>对齐 DSH [Service.invoke]：把服务包成可调用对象</para>
/// <para>源码生成器扫描此特性生成调用包装（后续扩展）</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ServiceInvokeAttribute : Attribute
{
}

/// <summary>
/// 可调用服务包装 — 把服务方法包成委托直接调用
/// <para>对齐 DSH ctx.logger() 式：ctx.MyService(arg) 直接调用</para>
/// </summary>
public sealed class CallableService<TArg, TResult>
{
    private readonly Func<TArg, TResult> _func;

    /// <param name="func">服务调用委托</param>
    public CallableService(Func<TArg, TResult> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        _func = func;
    }

    /// <summary>调用服务</summary>
    public TResult Call(TArg arg) => _func(arg);
}

/// <summary>
/// 可调用服务包装（无参数版本）
/// </summary>
public sealed class CallableService<TResult>
{
    private readonly Func<TResult> _func;

    /// <param name="func">服务调用委托</param>
    public CallableService(Func<TResult> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        _func = func;
    }

    /// <summary>调用服务</summary>
    public TResult Call() => _func();
}
