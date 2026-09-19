namespace Core.Utils;

/// <summary>
/// 过期值缓存 — 周期性刷新的值缓存，到期后下次访问触发刷新
/// </summary>
/// <typeparam name="T">值类型</typeparam>
public sealed class ExpiringValue<T> {
    private readonly Func<T> _refresh;
    private readonly long _intervalTicks;
    private T _value;
    private long _lastRefreshTicks;

    /// <summary>
    /// 构造过期值缓存
    /// </summary>
    /// <param name="refresh">刷新值的委托</param>
    /// <param name="interval">刷新间隔</param>
    public ExpiringValue(Func<T> refresh, TimeSpan interval) {
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _refresh = refresh;
        _intervalTicks = interval.Ticks;
        _value = default!;
    }

    /// <summary>
    /// 获取当前值；若已过期则触发刷新后返回新值
    /// </summary>
    /// <returns>当前值</returns>
    public T GetOrRefresh() {
        var now = Stopwatch.GetTimestamp();
        if (now - _lastRefreshTicks >= _intervalTicks) {
            _value = _refresh();
            _lastRefreshTicks = now;
        }
        return _value!;
    }

    /// <summary>
    /// 使缓存失效，下次 GetOrRefresh 强制刷新
    /// </summary>
    public void Invalidate() => _lastRefreshTicks = 0;
}