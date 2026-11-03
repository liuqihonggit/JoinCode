// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Infrastructure.Utils.Resilience;

/// <summary>
/// 熔断器相位枚举 — Closed(正常)/Open(熔断)/HalfOpen(半开探针)
/// </summary>
public enum CircuitBreakerPhase {
    /// <summary>关闭态 — 正常放行请求</summary>
    [EnumValue("closed")]
    Closed,
    /// <summary>开启态 — 熔断中,拒绝请求</summary>
    [EnumValue("open")]
    Open,
    /// <summary>半开态 — 限流放行探针请求以测试下游恢复</summary>
    [EnumValue("half_open")]
    HalfOpen
}

/// <summary>
/// 熔断器不可变状态快照 — 所有可变状态打包为 record，Volatile.Read 读 + Interlocked.CompareExchange CAS 更新
/// </summary>
internal sealed record CbState(
    CircuitBreakerPhase Phase,
    int ConsecutiveFailures,
    int TotalFailures,
    int TotalSuccesses,
    int HalfOpenProbeCount,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastFailureTime
) {
    public static readonly CbState Initial = new(
        CircuitBreakerPhase.Closed, 0, 0, 0, 0,
        DateTimeOffset.MinValue, DateTimeOffset.MinValue
    );
}

/// <summary>
/// 统一熔断器 — 不可变状态快照 + CAS 无锁更新（ADR 0040 企业级状态机）
/// <para>行为流程：Volatile.Read 快照 → 惰性 Open→HalfOpen → 纯函数计算下一状态 → Interlocked.CompareExchange CAS</para>
/// <para>计数器递增在状态转换前，惰性求值在读取 Phase 时触发 OpenTimeout</para>
/// </summary>
public sealed partial class UnifiedCircuitBreaker : IDisposable {
    private readonly TimeSpan _openDuration;
    private readonly int _failureThreshold;
    private readonly int _halfOpenMaxProbe;
    private CbState _state = CbState.Initial;
    private int _disposed;

    /// <summary>熔断器名称</summary>
    public string Name { get; }

    /// <summary>当前相位(别名 State)</summary>
    public CircuitBreakerPhase State => Phase;

    /// <summary>当前相位 — 读取时惰性触发 Open→HalfOpen 转换</summary>
    public CircuitBreakerPhase Phase => GetSnapshotWithLazyTransition().Phase;

    /// <summary>连续失败次数</summary>
    public int ConsecutiveFailures => Volatile.Read(ref _state).ConsecutiveFailures;

    /// <summary>累计失败次数</summary>
    public int TotalFailures => Volatile.Read(ref _state).TotalFailures;

    /// <summary>累计成功次数</summary>
    public int TotalSuccesses => Volatile.Read(ref _state).TotalSuccesses;

    /// <summary>熔断开启时间;未开启返回 null</summary>
    public DateTimeOffset? OpenedAt {
        get {
            var s = Volatile.Read(ref _state);
            return s.Phase != CircuitBreakerPhase.Closed ? s.OpenedAt : null;
        }
    }

    /// <summary>是否处于 Open 相位</summary>
    public bool IsOpen => Phase == CircuitBreakerPhase.Open;

    /// <summary>最近一次失败时间;无失败记录返回 null</summary>
    public DateTimeOffset? LastFailureTime {
        get {
            var s = Volatile.Read(ref _state);
            return s.LastFailureTime == DateTimeOffset.MinValue ? null : s.LastFailureTime;
        }
    }

    /// <summary>
    /// 构造统一熔断器
    /// </summary>
    /// <param name="name">熔断器名称,用于日志与锁标识</param>
    /// <param name="failureThreshold">连续失败阈值,达到即熔断</param>
    /// <param name="openDuration">熔断开启持续时长,超时进入 HalfOpen</param>
    /// <param name="halfOpenMaxProbe">半开态最大探针请求数</param>
    public UnifiedCircuitBreaker(string name, int failureThreshold = 5, TimeSpan? openDuration = null, int halfOpenMaxProbe = 1) {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failureThreshold);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(halfOpenMaxProbe);

        Name = name;
        _failureThreshold = failureThreshold;
        _openDuration = openDuration ?? TimeSpan.FromSeconds(30);
        _halfOpenMaxProbe = halfOpenMaxProbe;
    }

    /// <summary>
    /// 构造统一熔断器 — 从配置对象读取参数
    /// </summary>
    /// <param name="name">熔断器名称</param>
    /// <param name="config">熔断器配置</param>
    public UnifiedCircuitBreaker(string name, CircuitBreakerConfig config)
        : this(name, config.FailureThreshold, config.OpenDuration, config.HalfOpenMaxProbe) {
    }

    /// <summary>
    /// 尝试探针请求 — Closed 直接放行;HalfOpen 限流放行;Open 拒绝
    /// </summary>
    /// <returns>放行返回 true,拒绝返回 false</returns>
    public bool TryProbe() {
        while (true) {
            var snapshot = GetSnapshotWithLazyTransition();

            if (snapshot.Phase == CircuitBreakerPhase.Closed)
                return true;

            if (snapshot.Phase == CircuitBreakerPhase.HalfOpen) {
                if (snapshot.HalfOpenProbeCount >= _halfOpenMaxProbe)
                    return false;

                var next = snapshot with { HalfOpenProbeCount = snapshot.HalfOpenProbeCount + 1 };
                if (Interlocked.CompareExchange(ref _state, next, snapshot) == snapshot)
                    return true;
                continue;
            }

            return false;
        }
    }

    /// <summary>记录成功 — 任意状态回到 Closed 并清零失败计数</summary>
    public void RecordSuccess() {
        while (true) {
            var snapshot = GetSnapshotWithLazyTransition();
            var next = snapshot with {
                TotalSuccesses = snapshot.TotalSuccesses + 1,
                ConsecutiveFailures = 0,
                HalfOpenProbeCount = 0,
                Phase = CircuitBreakerPhase.Closed
            };
            if (Interlocked.CompareExchange(ref _state, next, snapshot) == snapshot) return;
        }
    }

    /// <summary>记录失败 — 累加失败计数,达阈值则熔断</summary>
    public void RecordFailure() {
        while (true) {
            var snapshot = GetSnapshotWithLazyTransition();
            var now = DateTimeOffset.UtcNow;
            var consecutiveFailures = snapshot.ConsecutiveFailures + 1;

            var next = snapshot with {
                ConsecutiveFailures = consecutiveFailures,
                TotalFailures = snapshot.TotalFailures + 1,
                LastFailureTime = now
            };

            if ((snapshot.Phase == CircuitBreakerPhase.Closed && consecutiveFailures >= _failureThreshold) ||
                snapshot.Phase == CircuitBreakerPhase.HalfOpen) {
                next = next with {
                    Phase = CircuitBreakerPhase.Open,
                    OpenedAt = now,
                    HalfOpenProbeCount = 0
                };
            }

            if (Interlocked.CompareExchange(ref _state, next, snapshot) == snapshot) return;
        }
    }

    /// <summary>手动重置 — 任意状态回到 Closed 并清零所有计数</summary>
    public void Reset() {
        while (true) {
            var snapshot = Volatile.Read(ref _state);
            var next = snapshot with {
                Phase = CircuitBreakerPhase.Closed,
                ConsecutiveFailures = 0,
                HalfOpenProbeCount = 0,
                OpenedAt = DateTimeOffset.MinValue
            };
            if (Interlocked.CompareExchange(ref _state, next, snapshot) == snapshot) return;
        }
    }

    /// <summary>
    /// 惰性求值 — Open 状态超时则 CAS 转换到 HalfOpen，返回最新快照
    /// </summary>
    private CbState GetSnapshotWithLazyTransition() {
        while (true) {
            var snapshot = Volatile.Read(ref _state);
            if (snapshot.Phase != CircuitBreakerPhase.Open ||
                DateTimeOffset.UtcNow - snapshot.OpenedAt <= _openDuration) {
                return snapshot;
            }
            var next = snapshot with {
                Phase = CircuitBreakerPhase.HalfOpen,
                HalfOpenProbeCount = 0
            };
            if (Interlocked.CompareExchange(ref _state, next, snapshot) == snapshot) {
                return next;
            }
        }
    }

    /// <summary>
    /// 释放资源 — 标记已释放
    /// </summary>
    public void Dispose() {
        Interlocked.Exchange(ref _disposed, 1);
    }
}
