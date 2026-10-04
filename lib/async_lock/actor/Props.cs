namespace Core.Utils;

/// <summary>
/// Actor 创建配置 — 封装 Actor 工厂委托 + 可选监督策略(Akka Props 对齐)。
/// <para>用途:解耦 Actor 创建与配置,支持 ActorSystem.ActorOf(props, name) 统一创建。</para>
/// <para>用法:<c>Props.Create(() => new MyActor(arg1, arg2))</c> 创建 Props,<c>props.Create()</c> 实例化。</para>
/// </summary>
/// <typeparam name="TActor">Actor 类型(需实现 IAsyncDisposable)</typeparam>
public sealed class Props<TActor> where TActor : IAsyncDisposable {
    private readonly Func<TActor> _factory;

    /// <summary>监督策略(null=使用父 Actor 默认策略)</summary>
    public SupervisorStrategy? SupervisorStrategy { get; init; }

    /// <summary>构造 Props</summary>
    /// <param name="factory">Actor 创建工厂</param>
    public Props(Func<TActor> factory) {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>创建 Actor 实例</summary>
    /// <returns>Actor 实例</returns>
    public TActor Create() => _factory();
}

/// <summary>
/// Props 工厂 — 创建 Props 实例(Akka Props.Create 对齐)。
/// </summary>
public static class Props {
    /// <summary>创建 Props — 从工厂委托</summary>
    /// <typeparam name="TActor">Actor 类型</typeparam>
    /// <param name="factory">Actor 创建工厂</param>
    /// <returns>Props 实例</returns>
    public static Props<TActor> Create<TActor>(Func<TActor> factory) where TActor : IAsyncDisposable
        => new(factory);
}
