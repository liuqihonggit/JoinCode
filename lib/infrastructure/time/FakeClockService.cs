namespace Infrastructure.Time;

/// <summary>
/// 假时钟服务 — 支持手动推进时间，用于调试和 E2E 测试
/// JCC_CLOCK_MODE=Fake 时激活
/// </summary>
public sealed class FakeClockService : IClockService
{
    private readonly FakeTimeProvider _timeProvider;

    /// <summary>
    /// 构造假时钟服务
    /// </summary>
    /// <param name="initialTime">初始时间；null 时使用当前 UTC 时间</param>
    public FakeClockService(DateTimeOffset? initialTime = null)
    {
        _timeProvider = new FakeTimeProvider(initialTime ?? DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 时间提供者
    /// </summary>
    public TimeProvider TimeProvider => _timeProvider;

    /// <summary>
    /// 获取当前 UTC 时间（DateTime 形式）
    /// </summary>
    /// <returns>当前 UTC 时间</returns>
    public DateTime GetUtcNow() => _timeProvider.GetUtcNow().DateTime;

    /// <summary>
    /// 获取当前本地时间
    /// </summary>
    /// <returns>当前本地时间</returns>
    public DateTime GetLocalNow() => _timeProvider.GetLocalNow().DateTime;

    /// <summary>
    /// 获取当前 UTC 时间（DateTimeOffset 形式）
    /// </summary>
    /// <returns>当前 UTC 时间偏移量</returns>
    public DateTimeOffset GetUtcNowOffset() => _timeProvider.GetUtcNow();

    /// <summary>
    /// 手动推进时间
    /// </summary>
    /// <param name="delta">推进的时间增量</param>
    public void Advance(TimeSpan delta) => _timeProvider.Advance(delta);

    /// <summary>
    /// 设置当前时间
    /// </summary>
    /// <param name="value">要设置的 UTC 时间</param>
    public void SetUtcNow(DateTimeOffset value) => _timeProvider.SetUtcNow(value);
}
