namespace Infrastructure.Time;

/// <summary>
/// 物理时钟服务 — 使用系统真实时间
/// </summary>
[Register(typeof(IClockService), ServiceLifetime.Singleton)]
public sealed partial class PhysicalClockService : ServiceEntity, IClockService {
    /// <inheritdoc/>
    public TimeProvider TimeProvider => TimeProvider.System;

    /// <inheritdoc/>
    public DateTime GetUtcNow() => DateTime.UtcNow;

    /// <inheritdoc/>
    public DateTime GetLocalNow() => DateTime.Now;

    /// <inheritdoc/>
    public DateTimeOffset GetUtcNowOffset() => DateTimeOffset.UtcNow;
}