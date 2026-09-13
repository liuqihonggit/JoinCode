namespace Core.Utils;

/// <summary>
/// 指数退避策略 — 根据重试次数计算 BaseDelay * 2^shift 的退避时长，受 MaxDelay 上限约束
/// </summary>
public sealed class ExponentialBackoff
{
    /// <summary>
    /// 基础延迟
    /// </summary>
    public TimeSpan BaseDelay { get; }

    /// <summary>
    /// 最大延迟上限
    /// </summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>
    /// 最大位移位数（限制指数增长上限，避免溢出）
    /// </summary>
    public int MaxShiftBits { get; }

    /// <summary>
    /// 默认实例：基础延迟 200ms，最大延迟 30s，最大位移 5 位
    /// </summary>
    public static ExponentialBackoff Default { get; } = new(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(30), 5);

    /// <summary>
    /// 构造指数退避策略
    /// </summary>
    /// <param name="baseDelay">基础延迟</param>
    /// <param name="maxDelay">最大延迟上限</param>
    /// <param name="maxShiftBits">最大位移位数</param>
    public ExponentialBackoff(TimeSpan baseDelay, TimeSpan maxDelay, int maxShiftBits = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(baseDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDelay, baseDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxShiftBits, 1);

        BaseDelay = baseDelay;
        MaxDelay = maxDelay;
        MaxShiftBits = maxShiftBits;
    }

    /// <summary>
    /// 根据重试次数计算退避时长：min(BaseDelay * 2^min(retryCount, MaxShiftBits), MaxDelay)
    /// </summary>
    /// <param name="retryCount">重试次数（从 0 开始）</param>
    /// <returns>退避时长</returns>
    public TimeSpan CalculateDelay(int retryCount)
    {
        var shift = Math.Min(retryCount, MaxShiftBits);
        var ms = Math.Min(BaseDelay.TotalMilliseconds * (1 << shift), MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(ms);
    }
}
