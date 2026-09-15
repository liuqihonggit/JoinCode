namespace JoinCode.Hands.Desktop;

/// <summary>
/// 窗口震动去抖协调器 — 进程内单例，1 秒去抖合并多个子代理的震动请求。
/// 用 <c>Interlocked.CompareExchange(ref int, int, int)</c> 无锁实现，避免锁竞争。
/// </summary>
[Register(typeof(IWindowShakeCoordinator), ServiceLifetime.Singleton)]
public sealed class WindowShakeCoordinator : ServiceEntity, IWindowShakeCoordinator
{
    /// <summary>
    /// 上次震动的 <see cref="Environment.TickCount"/> — volatile 保证可见性，Interlocked 保证原子性。
    /// </summary>
    private volatile int _lastShakeTick;

    /// <summary>
    /// 震动最小间隔（毫秒） — 1 秒去抖。
    /// </summary>
    private const int ShakeIntervalMs = 1000;

    /// <summary>
    /// 尝试获取震动时间槽。1 秒内只允许一次成功。
    /// </summary>
    /// <returns>true 表示可执行震动；false 表示 1 秒内已震动过。</returns>
    public bool TryAcquireShakeSlot()
    {
        var now = Environment.TickCount;
        var last = _lastShakeTick;
        if (now - last < ShakeIntervalMs)
            return false;
        return Interlocked.CompareExchange(ref _lastShakeTick, now, last) == last;
    }
}
