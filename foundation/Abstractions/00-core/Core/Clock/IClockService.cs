namespace JoinCode.Abstractions.Clock;

/// <summary>
/// 时钟服务 — 替代直接 DateTime.UtcNow/DateTimeOffset.UtcNow，支持环境变量一键切换 Real/Fake
/// <para>基于 TimeProvider 抽象，消费方通过 GetUtcNow()/GetLocalNow() 获取时间</para>
/// <para>FakeClockService 支持手动推进时间，用于调试和 E2E 测试</para>
/// </summary>
public interface IClockService
{
    /// <summary>
    /// 获取底层 TimeProvider（用于需要 TimeProvider 参数的 API，如 Task.Delay、Timer 等）
    /// </summary>
    TimeProvider TimeProvider { get; }

    /// <summary>
    /// 获取当前 UTC 时间 — 替代 DateTime.UtcNow
    /// </summary>
    DateTime GetUtcNow();

    /// <summary>
    /// 获取当前本地时间 — 替代 DateTime.Now
    /// </summary>
    DateTime GetLocalNow();

    /// <summary>
    /// 获取当前 UTC 偏移时间 — 替代 DateTimeOffset.UtcNow
    /// </summary>
    DateTimeOffset GetUtcNowOffset();
}
