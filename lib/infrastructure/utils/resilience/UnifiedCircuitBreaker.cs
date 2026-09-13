namespace Infrastructure.Utils.Resilience;

/// <summary>
/// 熔断器相位枚举 — Closed(正常)/Open(熔断)/HalfOpen(半开探针)
/// </summary>
public enum CircuitBreakerPhase
{
    /// <summary>关闭态 — 正常放行请求</summary>
    Closed,
    /// <summary>开启态 — 熔断中,拒绝请求</summary>
    Open,
    /// <summary>半开态 — 限流放行探针请求以测试下游恢复</summary>
    HalfOpen
}

/// <summary>
/// 熔断器事件枚举 — 驱动状态机转换（ADR 0040）
/// </summary>
public enum CircuitBreakerEvent
{
    /// <summary>记录成功 — 任意状态 → Closed</summary>
    RecordSuccess,

    /// <summary>记录失败 — Closed → Open(达阈值) 或 HalfOpen → Open</summary>
    RecordFailure,

    /// <summary>探针请求 — HalfOpen 下限流</summary>
    TryProbe,

    /// <summary>Open 超时 → HalfOpen</summary>
    OpenTimeout,

    /// <summary>重置 → Closed</summary>
    Reset,
}

/// <summary>
/// 熔断器共享上下文 — ADR 0040 FsmContext 强类型子类
/// </summary>
internal sealed class CircuitBreakerContext : FsmContext
{
    /// <summary>连续失败次数</summary>
    public int ConsecutiveFailures;
    /// <summary>累计失败次数</summary>
    public int TotalFailures;
    /// <summary>累计成功次数</summary>
    public int TotalSuccesses;
    /// <summary>半开态已放行的探针请求计数</summary>
    public int HalfOpenProbeCount;
    /// <summary>熔断开启时间</summary>
    public DateTimeOffset OpenedAt;
    /// <summary>最近一次失败时间</summary>
    public DateTimeOffset LastFailureTime;
    /// <summary>当前时间（由调用方注入，用于守卫判定）</summary>
    public DateTimeOffset Now;
    /// <summary>连续失败熔断阈值</summary>
    public int FailureThreshold;
    /// <summary>半开态最大探针请求数</summary>
    public int HalfOpenMaxProbe;
}

/// <summary>
/// 统一熔断器 — 转换表 + 守卫 + 共享上下文（ADR 0040 企业级状态机）
/// <para>行为流程：获取当前状态 → 查表 → 守卫判定 → 执行动作 → 转移</para>
/// <para>计数器递增在 Fsm.Trigger 之前（状态机外），惰性求值在读取 Phase 时触发 OpenTimeout</para>
/// </summary>
[FsmStateMachine(typeof(CircuitBreakerPhase), typeof(CircuitBreakerEvent), CircuitBreakerPhase.Closed)]
[Transition(CircuitBreakerPhase.Closed, CircuitBreakerEvent.RecordFailure, CircuitBreakerPhase.Open)]
[Transition(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.RecordFailure, CircuitBreakerPhase.Open)]
[Transition(CircuitBreakerPhase.Closed, CircuitBreakerEvent.RecordSuccess, CircuitBreakerPhase.Closed)]
[Transition(CircuitBreakerPhase.Open, CircuitBreakerEvent.RecordSuccess, CircuitBreakerPhase.Closed)]
[Transition(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.RecordSuccess, CircuitBreakerPhase.Closed)]
[Transition(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.TryProbe, CircuitBreakerPhase.HalfOpen)]
[Transition(CircuitBreakerPhase.Open, CircuitBreakerEvent.OpenTimeout, CircuitBreakerPhase.HalfOpen)]
[Transition(CircuitBreakerPhase.Closed, CircuitBreakerEvent.Reset, CircuitBreakerPhase.Closed)]
[Transition(CircuitBreakerPhase.Open, CircuitBreakerEvent.Reset, CircuitBreakerPhase.Closed)]
[Transition(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.Reset, CircuitBreakerPhase.Closed)]
public sealed partial class UnifiedCircuitBreaker
{
    private readonly TimeSpan _openDuration;
    private readonly AsyncLock _lock = new("UnifiedCircuitBreaker");
    private readonly Fsm<CircuitBreakerPhase, CircuitBreakerEvent> _fsm;
    private readonly CircuitBreakerContext _ctx;

    /// <summary>熔断器名称</summary>
    public string Name { get; }

    /// <summary>当前相位(别名 State)</summary>
    public CircuitBreakerPhase State => Phase;

    /// <summary>当前相位 — 读取时惰性触发 Open→HalfOpen 转换</summary>
    public CircuitBreakerPhase Phase
    {
        get
        {
            using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
            {
                MaybeTransitionToHalfOpen();
                return _fsm.CurrentState;
            }
        }
    }

    /// <summary>连续失败次数</summary>
    public int ConsecutiveFailures
    {
        get { using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) { return _ctx.ConsecutiveFailures; } }
    }

    /// <summary>累计失败次数</summary>
    public int TotalFailures
    {
        get { using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) { return _ctx.TotalFailures; } }
    }

    /// <summary>累计成功次数</summary>
    public int TotalSuccesses
    {
        get { using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) { return _ctx.TotalSuccesses; } }
    }

    /// <summary>熔断开启时间;未开启返回 null</summary>
    public DateTimeOffset? OpenedAt
    {
        get
        {
            using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
            {
                return _fsm.CurrentState != CircuitBreakerPhase.Closed ? _ctx.OpenedAt : null;
            }
        }
    }

    /// <summary>是否处于 Open 相位</summary>
    public bool IsOpen => Phase == CircuitBreakerPhase.Open;

    /// <summary>最近一次失败时间;无失败记录返回 null</summary>
    public DateTimeOffset? LastFailureTime
    {
        get { using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) { return _ctx.LastFailureTime == DateTimeOffset.MinValue ? null : _ctx.LastFailureTime; } }
    }

    /// <summary>
    /// 构造统一熔断器
    /// </summary>
    /// <param name="name">熔断器名称,用于日志与锁标识</param>
    /// <param name="failureThreshold">连续失败阈值,达到即熔断</param>
    /// <param name="openDuration">熔断开启持续时长,超时进入 HalfOpen</param>
    /// <param name="halfOpenMaxProbe">半开态最大探针请求数</param>
    public UnifiedCircuitBreaker(string name, int failureThreshold = 5, TimeSpan? openDuration = null, int halfOpenMaxProbe = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failureThreshold);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(halfOpenMaxProbe);

        Name = name;
        _openDuration = openDuration ?? TimeSpan.FromSeconds(30);
        _ctx = new CircuitBreakerContext
        {
            FailureThreshold = failureThreshold,
            HalfOpenMaxProbe = halfOpenMaxProbe,
            OpenedAt = DateTimeOffset.MinValue,
            LastFailureTime = DateTimeOffset.MinValue,
        };
        _fsm = new Fsm<CircuitBreakerPhase, CircuitBreakerEvent>(_fsmSortedKeys, _fsmRules, CircuitBreakerPhase.Closed);
        _fsm.StateChanged += (_, e) => FsmDispatchEvent(e);
    }

    /// <summary>
    /// 构造统一熔断器 — 从配置对象读取参数
    /// </summary>
    /// <param name="name">熔断器名称</param>
    /// <param name="config">熔断器配置</param>
    public UnifiedCircuitBreaker(string name, CircuitBreakerConfig config)
        : this(name, config.FailureThreshold, config.OpenDuration, config.HalfOpenMaxProbe)
    {
    }

    /// <summary>
    /// 尝试探针请求 — Closed 直接放行;HalfOpen 限流放行;Open 拒绝
    /// </summary>
    /// <returns>放行返回 true,拒绝返回 false</returns>
    public bool TryProbe()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            MaybeTransitionToHalfOpen();
            var state = _fsm.CurrentState;

            if (state == CircuitBreakerPhase.Closed)
                return true;

            if (state == CircuitBreakerPhase.HalfOpen)
            {
                _ctx.Now = DateTimeOffset.UtcNow;
                var result = _fsm.Trigger(CircuitBreakerEvent.TryProbe, _ctx);
                return result.Transitioned;
            }

            return false;
        }
    }

    /// <summary>记录成功 — 任意状态回到 Closed 并清零失败计数</summary>
    public void RecordSuccess()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            MaybeTransitionToHalfOpen();
            _ctx.TotalSuccesses++;
            _fsm.Trigger(CircuitBreakerEvent.RecordSuccess, _ctx);
        }
    }

    /// <summary>记录失败 — 累加失败计数,达阈值则熔断</summary>
    public void RecordFailure()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            MaybeTransitionToHalfOpen();

            _ctx.ConsecutiveFailures++;
            _ctx.TotalFailures++;
            _ctx.Now = DateTimeOffset.UtcNow;
            _ctx.LastFailureTime = _ctx.Now;

            _fsm.Trigger(CircuitBreakerEvent.RecordFailure, _ctx);
        }
    }

    /// <summary>手动重置 — 任意状态回到 Closed 并清零所有计数</summary>
    public void Reset()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            _fsm.Trigger(CircuitBreakerEvent.Reset, _ctx);
        }
    }

    /// <summary>
    /// 惰性求值 — Open 状态超时则触发 OpenTimeout 事件转 HalfOpen
    /// </summary>
    private void MaybeTransitionToHalfOpen()
    {
        if (_fsm.CurrentState == CircuitBreakerPhase.Open &&
            DateTimeOffset.UtcNow - _ctx.OpenedAt > _openDuration)
        {
            _ctx.Now = DateTimeOffset.UtcNow;
            _fsm.Trigger(CircuitBreakerEvent.OpenTimeout, _ctx);
        }
    }

    [Guard(CircuitBreakerPhase.Closed, CircuitBreakerEvent.RecordFailure)]
    private static bool FsmGuardFailuresExceedThreshold(FsmContext? ctx) => ((CircuitBreakerContext)ctx!).ConsecutiveFailures >= ((CircuitBreakerContext)ctx!).FailureThreshold;

    [Guard(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.TryProbe)]
    private static bool FsmGuardProbeCountUnderMax(FsmContext? ctx) => ((CircuitBreakerContext)ctx!).HalfOpenProbeCount < ((CircuitBreakerContext)ctx!).HalfOpenMaxProbe;

    [TransitionAction(CircuitBreakerPhase.Closed, CircuitBreakerEvent.RecordFailure)]
    [TransitionAction(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.RecordFailure)]
    private static void FsmActOpen(FsmContext? ctx)
    {
        var c = (CircuitBreakerContext)ctx!;
        c.OpenedAt = DateTimeOffset.UtcNow;
        c.HalfOpenProbeCount = 0;
    }

    [TransitionAction(CircuitBreakerPhase.Closed, CircuitBreakerEvent.RecordSuccess)]
    [TransitionAction(CircuitBreakerPhase.Open, CircuitBreakerEvent.RecordSuccess)]
    [TransitionAction(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.RecordSuccess)]
    private static void FsmActSuccess(FsmContext? ctx)
    {
        var c = (CircuitBreakerContext)ctx!;
        c.ConsecutiveFailures = 0;
        c.HalfOpenProbeCount = 0;
    }

    [TransitionAction(CircuitBreakerPhase.Closed, CircuitBreakerEvent.Reset)]
    [TransitionAction(CircuitBreakerPhase.Open, CircuitBreakerEvent.Reset)]
    [TransitionAction(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.Reset)]
    private static void FsmActReset(FsmContext? ctx)
    {
        var c = (CircuitBreakerContext)ctx!;
        c.ConsecutiveFailures = 0;
        c.HalfOpenProbeCount = 0;
        c.OpenedAt = DateTimeOffset.MinValue;
    }

    [TransitionAction(CircuitBreakerPhase.Open, CircuitBreakerEvent.OpenTimeout)]
    private static void FsmActHalfOpen(FsmContext? ctx) => ((CircuitBreakerContext)ctx!).HalfOpenProbeCount = 0;

    [TransitionAction(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.TryProbe)]
    private static void FsmActTryProbe(FsmContext? ctx) => ((CircuitBreakerContext)ctx!).HalfOpenProbeCount++;
}
