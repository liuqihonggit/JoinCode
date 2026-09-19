namespace Core.Utils;

/// <summary>
/// 断路器状态 — 跟踪连续失败次数和熔断开启时间窗口
/// </summary>
public sealed class CircuitBreakerState {
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private int _consecutiveFailures;
    private DateTime _lastFailureTime = DateTime.MinValue;

    /// <summary>
    /// 获取当前连续失败次数
    /// </summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>
    /// 获取断路器是否处于开启（熔断）状态
    /// </summary>
    public bool IsOpen => _consecutiveFailures >= _failureThreshold && DateTime.UtcNow - _lastFailureTime < _openDuration;

    /// <summary>
    /// 构造函数 — 指定失败阈值和熔断持续时间
    /// </summary>
    /// <param name="failureThreshold">连续失败阈值，必须为正</param>
    /// <param name="openDuration">熔断开启持续时间，必须为正</param>
    public CircuitBreakerState(int failureThreshold, TimeSpan openDuration) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failureThreshold);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(openDuration, TimeSpan.Zero);
        _failureThreshold = failureThreshold;
        _openDuration = openDuration;
    }

    /// <summary>
    /// 判断是否应触发熔断 — 达阈值且在熔断时间窗口内返回 true，窗口超时则重置计数
    /// </summary>
    /// <returns>是否应熔断</returns>
    public bool ShouldTrip() {
        if (_consecutiveFailures >= _failureThreshold) {
            var timeSinceLastFailure = DateTime.UtcNow - _lastFailureTime;
            if (timeSinceLastFailure < _openDuration)
                return true;

            _consecutiveFailures = 0;
        }

        return false;
    }

    /// <summary>
    /// 记录一次成功 — 重置连续失败计数
    /// </summary>
    public void RecordSuccess() => _consecutiveFailures = 0;

    /// <summary>
    /// 记录一次失败 — 递增连续失败计数并更新最后失败时间
    /// </summary>
    public void RecordFailure() {
        _consecutiveFailures++;
        _lastFailureTime = DateTime.UtcNow;
    }

    /// <summary>
    /// 重置状态 — 清零失败计数和最后失败时间
    /// </summary>
    public void Reset() {
        _consecutiveFailures = 0;
        _lastFailureTime = DateTime.MinValue;
    }
}