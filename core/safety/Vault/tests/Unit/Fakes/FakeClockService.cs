
namespace Core.Tests.Fakes;

/// <summary>
/// 测试用固定时钟 — 替代真实时间，便于断言 CreatedAt/UpdatedAt。
/// </summary>
internal sealed class FakeClockService : IClockService
{
    private DateTime _utcNow;

    public FakeClockService(DateTime? utcNow = null)
    {
        _utcNow = utcNow ?? new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);
    }

    public TimeProvider TimeProvider => TimeProvider.System;

    public DateTime GetUtcNow() => _utcNow;

    public DateTime GetLocalNow() => _utcNow.ToLocalTime();

    public DateTimeOffset GetUtcNowOffset() => new(_utcNow, TimeSpan.Zero);

    public void Advance(TimeSpan duration) => _utcNow += duration;

    public void SetUtcNow(DateTime value) => _utcNow = value;
}
