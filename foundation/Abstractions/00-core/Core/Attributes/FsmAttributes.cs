namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记类为状态机 — 声明状态枚举类型、事件枚举类型、初始状态
/// <para>ADR 0041: 源码生成器据此扫描 [Transition]/[Guard]/[Action] 特性生成转换表</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StateMachineAttribute : Attribute
{
    /// <summary>状态枚举类型</summary>
    public Type StateType { get; }

    /// <summary>事件枚举类型</summary>
    public Type EventType { get; }

    /// <summary>初始状态值</summary>
    public object InitialState { get; }

    public StateMachineAttribute(Type stateType, Type eventType, object initialState)
    {
        StateType = stateType;
        EventType = eventType;
        InitialState = initialState;
    }
}

/// <summary>
/// 声明状态转换 — From + Event → To
/// <para>类级特性，AllowMultiple=true 声明多条转换规则</para>
/// <para>枚举值通过 object 传递，源码生成器通过 TypedConstant 读取类型+值</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TransitionAttribute : Attribute
{
    /// <summary>源状态</summary>
    public object From { get; }

    /// <summary>触发事件</summary>
    public object Event { get; }

    /// <summary>目标状态</summary>
    public object To { get; }

    public TransitionAttribute(object from, object evt, object to)
    {
        From = from;
        Event = evt;
        To = to;
    }
}

/// <summary>
/// 标记方法为转换守卫 — 签名: static bool MethodName(FsmContext? ctx)
/// <para>方法级特性，关联到 (From, Event) 转换</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class GuardAttribute : Attribute
{
    /// <summary>源状态</summary>
    public object From { get; }

    /// <summary>触发事件</summary>
    public object Event { get; }

    public GuardAttribute(object from, object evt)
    {
        From = from;
        Event = evt;
    }
}

/// <summary>
/// 标记方法为转换动作 — 签名: static void MethodName(FsmContext? ctx)
/// <para>方法级特性，关联到 (From, Event) 转换，转换成功后执行</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class TransitionActionAttribute : Attribute
{
    /// <summary>源状态</summary>
    public object From { get; }

    /// <summary>触发事件</summary>
    public object Event { get; }

    public TransitionActionAttribute(object from, object evt)
    {
        From = from;
        Event = evt;
    }
}
