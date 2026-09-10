namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 插件 Fiber 状态 — 对齐 Cordis Fiber 状态机
/// <para>PENDING → ACTIVATING → ACTIVE → UNLOADING → UNLOADED</para>
/// <para>ACTIVATING → FAILED(激活失败)</para>
/// <para>ACTIVE → FAILED(运行失败)</para>
/// <para>FAILED → ACTIVATING(重试激活) | UNLOADING(卸载失败插件)</para>
/// <para>UNLOADING → FAILED(卸载失败)</para>
/// </summary>
public enum PluginFiberState
{
    /// <summary>已声明,依赖未就绪</summary>
    Pending,
    /// <summary>激活中,Activate 运行中</summary>
    Activating,
    /// <summary>运行中</summary>
    Active,
    /// <summary>激活/运行/卸载失败</summary>
    Failed,
    /// <summary>卸载中,撤销链执行中</summary>
    Unloading,
    /// <summary>已卸载,资源全部释放</summary>
    Unloaded,
}

/// <summary>
/// Fiber 状态机 — 非法转换抛 InvalidOperationException[INF-FIBER-ILLEGAL]
/// <para>对齐 Cordis:状态机约束插件生命周期,非法转换立即报错而非静默继续</para>
/// <para>内部复用 StateMachine&lt;TState&gt; 基础设施,消除手写锁/转换表/事件重复逻辑</para>
/// <para>FAILED → ACTIVATING 允许失败后重试激活(ADR 0098 融合)</para>
/// </summary>
public sealed class PluginFiber
{
    private static readonly FrozenDictionary<PluginFiberState, FrozenSet<PluginFiberState>> Transitions = CreateTransitionTable();
    private readonly StateMachine<PluginFiberState> _stateMachine = new(Transitions, PluginFiberState.Pending);

    /// <summary>当前状态</summary>
    public PluginFiberState State => _stateMachine.CurrentState;

    /// <summary>转换状态 — 非法转换抛 InvalidOperationException</summary>
    public void TransitionTo(PluginFiberState next)
    {
        try
        {
            _stateMachine.TransitionTo(next);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"[INF-FIBER-ILLEGAL] 非法状态转换: {_stateMachine.CurrentState} → {next}");
        }
    }

    /// <summary>尝试转换状态 — 非法转换返回 false(不抛)</summary>
    public bool TryTransitionTo(PluginFiberState next) => _stateMachine.TryTransitionTo(next);

    private static FrozenDictionary<PluginFiberState, FrozenSet<PluginFiberState>> CreateTransitionTable()
    {
        return new Dictionary<PluginFiberState, FrozenSet<PluginFiberState>>
        {
            [PluginFiberState.Pending] = FrozenSet.Create(PluginFiberState.Activating, PluginFiberState.Unloading),
            [PluginFiberState.Activating] = FrozenSet.Create(PluginFiberState.Active, PluginFiberState.Failed),
            [PluginFiberState.Active] = FrozenSet.Create(PluginFiberState.Unloading, PluginFiberState.Failed),
            [PluginFiberState.Failed] = FrozenSet.Create(PluginFiberState.Activating, PluginFiberState.Unloading),
            [PluginFiberState.Unloading] = FrozenSet.Create(PluginFiberState.Unloaded, PluginFiberState.Failed),
            [PluginFiberState.Unloaded] = FrozenSet<PluginFiberState>.Empty,
        }.ToFrozenDictionary();
    }
}
