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
/// </summary>
public sealed class PluginFiber
{
    private PluginFiberState _state = PluginFiberState.Pending;
    private readonly object _lock = new();

    /// <summary>当前状态</summary>
    public PluginFiberState State => _state;

    /// <summary>转换状态 — 非法转换抛 InvalidOperationException</summary>
    public void TransitionTo(PluginFiberState next)
    {
        lock (_lock)
        {
            if (!IsValidTransition(_state, next))
            {
                throw new InvalidOperationException(
                    $"[INF-FIBER-ILLEGAL] 非法状态转换: {_state} → {next}");
            }
            _state = next;
        }
    }

    /// <summary>尝试转换状态 — 非法转换返回 false(不抛)</summary>
    public bool TryTransitionTo(PluginFiberState next)
    {
        lock (_lock)
        {
            if (!IsValidTransition(_state, next)) return false;
            _state = next;
            return true;
        }
    }

    private static bool IsValidTransition(PluginFiberState from, PluginFiberState to) => (from, to) switch
    {
        (PluginFiberState.Pending, PluginFiberState.Loading) => true,
        (PluginFiberState.Loading, PluginFiberState.Active) => true,
        (PluginFiberState.Loading, PluginFiberState.Failed) => true,
        (PluginFiberState.Pending, PluginFiberState.Unloading) => true,
        (PluginFiberState.Active, PluginFiberState.Unloading) => true,
        (PluginFiberState.Failed, PluginFiberState.Unloading) => true,
        (PluginFiberState.Unloading, PluginFiberState.Disposed) => true,
        (PluginFiberState.Failed, PluginFiberState.Disposed) => true,
        _ => false,
    };
}
