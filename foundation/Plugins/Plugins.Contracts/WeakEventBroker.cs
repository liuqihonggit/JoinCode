namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 弱事件代理 — 真正的弱事件(ADR 0098)
/// <para>订阅者死亡自动回收,ConditionalWeakTable 以事件源为键,源回收后条目自动消失</para>
/// <para>AOT 兼容:不用反射,用 Action&lt;TTarget, TArgs&gt; 委托(target 作为参数传入,不捕获 target)</para>
/// </summary>
public static class WeakEventBroker<TArgs>
{
    private static readonly ConditionalWeakTable<object, List<WeakHandler<TArgs>>> _table = new();

    /// <summary>
    /// 订阅事件 — handler 不捕获 target(target 作为参数传入),实现真正弱引用
    /// </summary>
    /// <param name="source">事件源(ConditionalWeakTable 键,源回收后条目自动消失)</param>
    /// <param name="target">订阅者(弱引用目标,死亡后自动回收)</param>
    /// <param name="handler">回调(target 作为参数传入,不捕获 target)</param>
    public static void Subscribe<TTarget>(object source, TTarget target, Action<TTarget, TArgs> handler) where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(handler);
        var list = _table.GetValue(source, _ => new List<WeakHandler<TArgs>>());
        list.Add(new WeakHandler<TTarget, TArgs>(target, handler));
    }

    /// <summary>
    /// 触发事件 — 遍历订阅者,弱引用 target 死亡则移除
    /// </summary>
    public static void Dispatch(object source, TArgs args)
    {
        if (!_table.TryGetValue(source, out var list)) return;
        var survivors = new List<WeakHandler<TArgs>>(list.Count);
        foreach (var handler in list)
        {
            if (InvokeSafe(handler, args))
                survivors.Add(handler);
        }
        if (survivors.Count != list.Count)
        {
            list.Clear();
            list.AddRange(survivors);
        }
    }

    private static bool InvokeSafe(WeakHandler<TArgs> handler, TArgs args)
    {
        try
        {
            return handler.Invoke(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WeakEventBroker] 订阅者异常: {ex.Message}");
            return true;
        }
    }

    /// <summary>获取订阅者数量(测试/诊断用)</summary>
    public static int GetSubscriberCount(object source)
        => _table.TryGetValue(source, out var list) ? list.Count : 0;
}

internal abstract class WeakHandler<TArgs>
{
    /// <summary>调用处理器,返回 false 表示 target 已死亡</summary>
    public abstract bool Invoke(TArgs args);
}

internal sealed class WeakHandler<TTarget, TArgs> : WeakHandler<TArgs> where TTarget : class
{
    private readonly WeakReference<TTarget> _targetRef;
    private readonly Action<TTarget, TArgs> _callback;

    public WeakHandler(TTarget target, Action<TTarget, TArgs> callback)
    {
        _targetRef = new WeakReference<TTarget>(target);
        _callback = callback;
    }

    public override bool Invoke(TArgs args)
    {
        if (_targetRef.TryGetTarget(out var target))
        {
            _callback(target, args);
            return true;
        }
        return false;
    }
}
