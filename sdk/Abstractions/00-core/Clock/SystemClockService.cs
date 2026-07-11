namespace JoinCode.Abstractions.Clock;

public sealed class SystemClockService : IClockService
{
    public static readonly SystemClockService Instance = new();

    public TimeProvider TimeProvider => TimeProvider.System;

    public DateTime GetUtcNow() => DateTime.UtcNow;

    public DateTime GetLocalNow() => DateTime.Now;

    public DateTimeOffset GetUtcNowOffset() => DateTimeOffset.UtcNow;
}
