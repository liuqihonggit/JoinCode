namespace Infrastructure.Time;

/// <summary>
/// 时钟服务工厂 — 根据 JCC_CLOCK_MODE 环境变量创建对应的 IClockService 实例
/// 用于 DI 容器构建之前的场景，DI 容器构建后应通过注入 IClockService 使用
/// </summary>
public static class ClockServiceFactory
{
    /// <summary>
    /// 根据环境变量创建 IClockService 实例。
    /// JCC_CLOCK_MODE=Fake → FakeClockService（可控时间，调试/E2E测试用）
    /// 其他/未设置 → PhysicalClockService（真实系统时间，默认）
    /// </summary>
    public static IClockService Create()
    {
        var mode = EnvHelper.Get(JccEnvVar.ClockMode);
        if (string.Equals(mode, "Fake", StringComparison.OrdinalIgnoreCase))
            return new FakeClockService();
        return new PhysicalClockService();
    }
}
