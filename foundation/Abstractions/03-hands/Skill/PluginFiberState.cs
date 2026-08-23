namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 插件 Fiber 状态 — 对齐 Cordis Fiber 状态机
/// <para>PENDING → LOADING → ACTIVE → UNLOADING → DISPOSED</para>
/// <para>LOADING → FAILED(加载失败)</para>
/// <para>FAILED → DISPOSED(失败后清理)</para>
/// </summary>
public enum PluginFiberState
{
    /// <summary>已声明,依赖未就绪</summary>
    Pending,
    /// <summary>依赖就绪,LoadAsync 运行中</summary>
    Loading,
    /// <summary>运行中</summary>
    Active,
    /// <summary>LoadAsync 抛异常</summary>
    Failed,
    /// <summary>卸载中,disposer 执行中</summary>
    Unloading,
    /// <summary>已卸载,资源全部释放</summary>
    Disposed,
}

/// <summary>
/// Fiber 状态机 — 非法转换抛 InvalidOperationException[INF-FIBER-ILLEGAL]
/// <para>对齐 Cordis:状态机约束插件生命周期,非法转换立即报错而非静默继续</para>
/// <para>内部复用 StateMachine&lt;TState&gt; 基础设施,消除手写锁/转换表/事件重复逻辑</para>
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
            [PluginFiberState.Pending] = FrozenSet.Create(PluginFiberState.Loading, PluginFiberState.Unloading),
            [PluginFiberState.Loading] = FrozenSet.Create(PluginFiberState.Active, PluginFiberState.Failed),
            [PluginFiberState.Active] = FrozenSet.Create(PluginFiberState.Unloading),
            [PluginFiberState.Failed] = FrozenSet.Create(PluginFiberState.Unloading, PluginFiberState.Disposed),
            [PluginFiberState.Unloading] = FrozenSet.Create(PluginFiberState.Disposed),
            [PluginFiberState.Disposed] = FrozenSet<PluginFiberState>.Empty,
        }.ToFrozenDictionary();
    }
}
