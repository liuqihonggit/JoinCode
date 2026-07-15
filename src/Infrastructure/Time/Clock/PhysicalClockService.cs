namespace Infrastructure.Time;

/// <summary>
/// 物理时钟服务 — 使用系统真实时间
/// </summary>
[Register(typeof(IClockService))]
public sealed partial class PhysicalClockService : IClockService
{
    public TimeProvider TimeProvider => TimeProvider.System;

    public DateTime GetUtcNow() => DateTime.UtcNow;

    public DateTime GetLocalNow() => DateTime.Now;

    public DateTimeOffset GetUtcNowOffset() => DateTimeOffset.UtcNow;
}
