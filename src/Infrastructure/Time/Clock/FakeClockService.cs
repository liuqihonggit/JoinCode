namespace Infrastructure.Time;

/// <summary>
/// 假时钟服务 — 支持手动推进时间，用于调试和 E2E 测试
/// JCC_CLOCK_MODE=Fake 时激活
/// </summary>
public sealed class FakeClockService : IClockService
{
    private readonly FakeTimeProvider _timeProvider;

    public FakeClockService(DateTimeOffset? initialTime = null)
    {
        _timeProvider = new FakeTimeProvider(initialTime ?? DateTimeOffset.UtcNow);
    }

    public TimeProvider TimeProvider => _timeProvider;

    public DateTime GetUtcNow() => _timeProvider.GetUtcNow().DateTime;

    public DateTime GetLocalNow() => _timeProvider.GetLocalNow().DateTime;

    public DateTimeOffset GetUtcNowOffset() => _timeProvider.GetUtcNow();

    /// <summary>
    /// 手动推进时间
    /// </summary>
    public void Advance(TimeSpan delta) => _timeProvider.Advance(delta);

    /// <summary>
    /// 设置当前时间
    /// </summary>
    public void SetUtcNow(DateTimeOffset value) => _timeProvider.SetUtcNow(value);
}
