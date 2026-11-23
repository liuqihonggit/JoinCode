namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 过期值缓存 — 周期性刷新的值缓存，到期后下次访问触发刷新
/// </summary>
/// <typeparam name="T">值类型</typeparam>
public sealed class ExpiringValue<T> {
    private readonly Func<T> _refresh;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _timeProvider;
    [AllowNull]
    private T _value;
    private long _lastRefreshTimestamp;
    private bool _hasValue;

    /// <summary>
    /// 构造过期值缓存
    /// </summary>
    /// <param name="refresh">刷新值的委托</param>
    /// <param name="interval">刷新间隔</param>
    /// <param name="timeProvider">时间源，默认使用系统时钟</param>
    public ExpiringValue(Func<T> refresh, TimeSpan interval, TimeProvider? timeProvider = null) {
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _refresh = refresh;
        _interval = interval;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _value = default;
    }

    /// <summary>
    /// 获取当前值；若已过期则触发刷新后返回新值
    /// </summary>
    /// <returns>当前值</returns>
    public T GetOrRefresh() {
        var now = _timeProvider.GetTimestamp();
        if (!_hasValue || _timeProvider.GetElapsedTime(_lastRefreshTimestamp, now) >= _interval) {
            _value = _refresh();
            _lastRefreshTimestamp = now;
            _hasValue = true;
        }
        return _value;
    }

    /// <summary>
    /// 使缓存失效，下次 GetOrRefresh 强制刷新
    /// </summary>
    public void Invalidate() => _hasValue = false;
}