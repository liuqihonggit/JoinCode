namespace Infrastructure.Time;

/// <summary>
/// 可控时间提供者 — 用于调试和 E2E 测试，支持手动推进/设置时间
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset initialTime)
    {
        _utcNow = initialTime;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long TimestampFrequency => Stopwatch.Frequency;

    /// <summary>
    /// 手动推进时间
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delta), "Advance delta must be non-negative");
        _utcNow = _utcNow.Add(delta);
    }

    /// <summary>
    /// 设置当前 UTC 时间
    /// </summary>
    public void SetUtcNow(DateTimeOffset value)
    {
        _utcNow = value;
    }
}
