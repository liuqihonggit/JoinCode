
namespace Core.Tests.Fakes;

/// <summary>
/// 测试用固定时钟 — 替代真实时间，便于断言 CreatedAt/UpdatedAt。
/// </summary>
public sealed class FakeClockService : IClockService {
    private DateTime _utcNow;

    /// <summary>构造固定时钟实例。</summary>
    /// <param name="utcNow">初始 UTC 时间，缺省为 2026-08-02 12:00:00。</param>
    public FakeClockService(DateTime? utcNow = null) {
        _utcNow = utcNow ?? new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>获取时间提供者。</summary>
    public TimeProvider TimeProvider => TimeProvider.System;

    /// <summary>获取当前 UTC 时间。</summary>
    public DateTime GetUtcNow() => _utcNow;

    /// <summary>获取当前本地时间。</summary>
    public DateTime GetLocalNow() => _utcNow.ToLocalTime();

    /// <summary>获取当前 UTC 时间偏移量。</summary>
    public DateTimeOffset GetUtcNowOffset() => new(_utcNow, TimeSpan.Zero);

    /// <summary>推进时钟指定时长。</summary>
    public void Advance(TimeSpan duration) => _utcNow += duration;

    /// <summary>设置当前 UTC 时间。</summary>
    public void SetUtcNow(DateTime value) => _utcNow = value;
}
