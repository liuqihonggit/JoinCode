namespace Infrastructure.Time;

/// <summary>
/// 可控时间提供者 — 用于调试和 E2E 测试，支持手动推进/设置时间
/// </summary>
public sealed class FakeTimeProvider : TimeProvider {
    private DateTimeOffset _utcNow;

    /// <summary>
    /// 构造可控时间提供者
    /// </summary>
    /// <param name="initialTime">初始 UTC 时间</param>
    public FakeTimeProvider(DateTimeOffset initialTime) {
        _utcNow = initialTime;
    }

    /// <summary>
    /// 获取当前 UTC 时间
    /// </summary>
    /// <returns>当前 UTC 时间偏移量</returns>
    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>
    /// 时间戳频率（与 Stopwatch.Frequency 一致）
    /// </summary>
    public override long TimestampFrequency => Stopwatch.Frequency;

    /// <summary>
    /// 手动推进时间
    /// </summary>
    /// <param name="delta">推进的时间增量（必须非负）</param>
    public void Advance(TimeSpan delta) {
        if (delta < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delta), "Advance delta must be non-negative");
        _utcNow = _utcNow.Add(delta);
    }

    /// <summary>
    /// 设置当前 UTC 时间
    /// </summary>
    /// <param name="value">要设置的 UTC 时间</param>
    public void SetUtcNow(DateTimeOffset value) {
        _utcNow = value;
    }
}