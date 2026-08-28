namespace Infrastructure.Utils.Resilience;

public enum CircuitBreakerPhase
{
    Closed,
    Open,
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
    public int ConsecutiveFailures;
    public int TotalFailures;
    public int TotalSuccesses;
    public int HalfOpenProbeCount;
    public DateTimeOffset OpenedAt;
    public DateTimeOffset LastFailureTime;
    public DateTimeOffset Now;
    public int FailureThreshold;
    public int HalfOpenMaxProbe;
}

/// <summary>
/// 统一熔断器 — 转换表 + 守卫 + 共享上下文（ADR 0040 企业级状态机）
/// <para>行为流程：获取当前状态 → 查表 → 守卫判定 → 执行动作 → 转移</para>
/// <para>计数器递增在 Fsm.Trigger 之前（状态机外），惰性求值在读取 Phase 时触发 OpenTimeout</para>
/// </summary>
public sealed class UnifiedCircuitBreaker
{
    private readonly TimeSpan _openDuration;
    private readonly object _lock = new();
    private readonly Fsm<CircuitBreakerPhase, CircuitBreakerEvent> _fsm;
    private readonly CircuitBreakerContext _ctx;

    public string Name { get; }

    public CircuitBreakerPhase State => Phase;

    public CircuitBreakerPhase Phase
    {
        get
        {
            lock (_lock)
            {
                MaybeTransitionToHalfOpen();
                return _fsm.CurrentState;
            }
        }
    }

    public int ConsecutiveFailures
    {
        get { lock (_lock) { return _ctx.ConsecutiveFailures; } }
    }

    public int TotalFailures
    {
        get { lock (_lock) { return _ctx.TotalFailures; } }
    }

    public int TotalSuccesses
    {
        get { lock (_lock) { return _ctx.TotalSuccesses; } }
    }

    public DateTimeOffset? OpenedAt
    {
        get
        {
            lock (_lock)
            {
                return _fsm.CurrentState != CircuitBreakerPhase.Closed ? _ctx.OpenedAt : null;
            }
        }
    }

    public bool IsOpen => Phase == CircuitBreakerPhase.Open;

    public DateTimeOffset? LastFailureTime
    {
        get { lock (_lock) { return _ctx.LastFailureTime == DateTimeOffset.MinValue ? null : _ctx.LastFailureTime; } }
    }

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
        _fsm = new Fsm<CircuitBreakerPhase, CircuitBreakerEvent>(CreateTransitionTable(), CircuitBreakerPhase.Closed);
    }

    public UnifiedCircuitBreaker(string name, CircuitBreakerConfig config)
        : this(name, config.FailureThreshold, config.OpenDuration, config.HalfOpenMaxProbe)
    {
    }

    public bool TryProbe()
    {
        lock (_lock)
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

    public void RecordSuccess()
    {
        lock (_lock)
        {
            MaybeTransitionToHalfOpen();
            _ctx.TotalSuccesses++;
            _fsm.Trigger(CircuitBreakerEvent.RecordSuccess, _ctx);
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            MaybeTransitionToHalfOpen();

            _ctx.ConsecutiveFailures++;
            _ctx.TotalFailures++;
            _ctx.Now = DateTimeOffset.UtcNow;
            _ctx.LastFailureTime = _ctx.Now;

            _fsm.Trigger(CircuitBreakerEvent.RecordFailure, _ctx);
        }
    }

    public void Reset()
    {
        lock (_lock)
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

    private FrozenDictionary<TransitionKey<CircuitBreakerPhase, CircuitBreakerEvent>, TransitionRule<CircuitBreakerPhase>> CreateTransitionTable()
    {
        TransitionAction openAction = ctx =>
        {
            var c = (CircuitBreakerContext)ctx!;
            c.OpenedAt = DateTimeOffset.UtcNow;
            c.HalfOpenProbeCount = 0;
        };

        TransitionAction successAction = ctx =>
        {
            var c = (CircuitBreakerContext)ctx!;
            c.ConsecutiveFailures = 0;
            c.HalfOpenProbeCount = 0;
        };

        TransitionAction resetAction = ctx =>
        {
            var c = (CircuitBreakerContext)ctx!;
            c.ConsecutiveFailures = 0;
            c.HalfOpenProbeCount = 0;
            c.OpenedAt = DateTimeOffset.MinValue;
        };

        TransitionAction halfOpenAction = ctx => ((CircuitBreakerContext)ctx!).HalfOpenProbeCount = 0;

        TransitionGuard failuresExceedThreshold = ctx => ((CircuitBreakerContext)ctx!).ConsecutiveFailures >= ((CircuitBreakerContext)ctx!).FailureThreshold;
        TransitionGuard probeCountUnderMax = ctx => ((CircuitBreakerContext)ctx!).HalfOpenProbeCount < ((CircuitBreakerContext)ctx!).HalfOpenMaxProbe;

        return new Dictionary<TransitionKey<CircuitBreakerPhase, CircuitBreakerEvent>, TransitionRule<CircuitBreakerPhase>>
        {
            [new(CircuitBreakerPhase.Closed, CircuitBreakerEvent.RecordFailure)] = new(CircuitBreakerPhase.Open, failuresExceedThreshold, openAction),
            [new(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.RecordFailure)] = new(CircuitBreakerPhase.Open, null, openAction),

            [new(CircuitBreakerPhase.Closed, CircuitBreakerEvent.RecordSuccess)] = new(CircuitBreakerPhase.Closed, null, successAction),
            [new(CircuitBreakerPhase.Open, CircuitBreakerEvent.RecordSuccess)] = new(CircuitBreakerPhase.Closed, null, successAction),
            [new(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.RecordSuccess)] = new(CircuitBreakerPhase.Closed, null, successAction),

            [new(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.TryProbe)] = new(CircuitBreakerPhase.HalfOpen, probeCountUnderMax, ctx => ((CircuitBreakerContext)ctx!).HalfOpenProbeCount++),

            [new(CircuitBreakerPhase.Open, CircuitBreakerEvent.OpenTimeout)] = new(CircuitBreakerPhase.HalfOpen, null, halfOpenAction),

            [new(CircuitBreakerPhase.Closed, CircuitBreakerEvent.Reset)] = new(CircuitBreakerPhase.Closed, null, resetAction),
            [new(CircuitBreakerPhase.Open, CircuitBreakerEvent.Reset)] = new(CircuitBreakerPhase.Closed, null, resetAction),
            [new(CircuitBreakerPhase.HalfOpen, CircuitBreakerEvent.Reset)] = new(CircuitBreakerPhase.Closed, null, resetAction),
        }.ToFrozenDictionary();
    }
}
