namespace Core.Bridge;

/// <summary>
/// BridgeMain 生命周期状态 — 归纳 _isDisposed(int)+_isShuttingDown(int) 双标志的合法组合
/// <para>_isResuming(bool) 和 _fatalExit(bool) 是关闭行为修饰符，非生命周期状态：</para>
/// <para>• _isResuming: 启动时设置(resumeSessionId!=null)，关闭时读取(跳过 archive+deregister)</para>
/// <para>• _fatalExit: 错误时设置(BridgeFatalError/退避放弃)，关闭时读取(跳过 resume 提示)</para>
/// </summary>
public enum BridgeMainLifecycleState
{
    /// <summary>初始状态 — RunAsync 调用前，_isDisposed=0 _isShuttingDown=0 _loopTask=null</summary>
    [EnumValue("created")] Created,

    /// <summary>运行中 — 主循环已启动，_loopTask 未完成</summary>
    [EnumValue("running")] Running,

    /// <summary>关闭中 — ShutdownAsync 已调用，_isShuttingDown=1 _isDisposed=0</summary>
    [EnumValue("shutting_down")] ShuttingDown,

    /// <summary>已释放 — DisposeAsync 已完成，_isDisposed=1（终态）</summary>
    [EnumValue("disposed")] Disposed
}

/// <summary>
/// BridgeMain 生命周期状态转换规则 — 集中定义 BridgeMainLifecycleState 所有合法转换
/// <para>Created→Running/ShuttingDown/Disposed, Running→ShuttingDown/Disposed,</para>
/// <para>ShuttingDown→Disposed, Disposed 为终态</para>
/// <para>DisposeAsync 内部调用 ShutdownAsync，因此 Created/Running→Disposed 为合法快捷路径</para>
/// </summary>
public static class BridgeMainLifecycleTransitions
{
    private static readonly FrozenDictionary<BridgeMainLifecycleState, FrozenSet<BridgeMainLifecycleState>> Transitions =
        new Dictionary<BridgeMainLifecycleState, FrozenSet<BridgeMainLifecycleState>>
        {
            [BridgeMainLifecycleState.Created] = new HashSet<BridgeMainLifecycleState>
            {
                BridgeMainLifecycleState.Running,
                BridgeMainLifecycleState.ShuttingDown,
                BridgeMainLifecycleState.Disposed
            }.ToFrozenSet(),

            [BridgeMainLifecycleState.Running] = new HashSet<BridgeMainLifecycleState>
            {
                BridgeMainLifecycleState.ShuttingDown,
                BridgeMainLifecycleState.Disposed
            }.ToFrozenSet(),

            [BridgeMainLifecycleState.ShuttingDown] = new HashSet<BridgeMainLifecycleState>
            {
                BridgeMainLifecycleState.Disposed
            }.ToFrozenSet(),

            [BridgeMainLifecycleState.Disposed] = FrozenSet<BridgeMainLifecycleState>.Empty
        }.ToFrozenDictionary();

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法（Interlocked.Exchange 幂等守卫）
    /// </summary>
    public static bool CanTransitionTo(BridgeMainLifecycleState current, BridgeMainLifecycleState target)
    {
        if (current == target)
        {
            return true;
        }

        return Transitions.TryGetValue(current, out var targets) && targets.Contains(target);
    }

    /// <summary>
    /// 是否为终态 — Disposed 为终态
    /// </summary>
    public static bool IsTerminal(BridgeMainLifecycleState state) => state == BridgeMainLifecycleState.Disposed;
}
