namespace JoinCode.Abstractions.Clock;

public sealed class SystemClockService : IClockService {
    public static readonly SystemClockService Instance = new();

    /// <summary>获取时间提供者。</summary>
    public TimeProvider TimeProvider => TimeProvider.System;

    /// <summary>获取当前 UTC 时间。</summary>
    public DateTime GetUtcNow() => DateTime.UtcNow;

    /// <summary>获取当前本地时间。</summary>
    public DateTime GetLocalNow() => DateTime.Now;

    /// <summary>获取当前 UTC 时间偏移量。</summary>
    public DateTimeOffset GetUtcNowOffset() => DateTimeOffset.UtcNow;
}