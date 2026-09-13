namespace Infrastructure.Subprocess;

/// <summary>
/// 子进程弹性策略配置 — 集中描述写入/读取超时、健康检查、最大重启次数与断路器参数
/// </summary>
public sealed class SubprocessResiliencePolicy
{
    /// <summary>策略名称，用于日志与断路器命名</summary>
    public required string Name { get; init; }

    /// <summary>写入标准输入的超时时间，默认 10 秒</summary>
    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>读取标准输出的超时时间，默认 30 秒</summary>
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>健康检查配置</summary>
    public HealthCheckConfig HealthCheck { get; init; } = new();

    /// <summary>最大重启次数，0 表示不自动重启</summary>
    public int MaxRestarts { get; init; } = 3;

    /// <summary>断路器配置，默认失败阈值 5、开启 60 秒</summary>
    public CircuitBreakerConfig CircuitBreaker { get; init; } = new()
    {
        FailureThreshold = 5,
        OpenDuration = TimeSpan.FromSeconds(60),
    };

    /// <summary>
    /// 以指定名称构造默认策略
    /// </summary>
    /// <param name="name">策略名称</param>
    /// <returns>默认配置的策略实例</returns>
    public static SubprocessResiliencePolicy Default(string name) => new() { Name = name };

    /// <summary>桥接子进程默认策略 — 10s 写超时、30s 读超时、最多 3 次重启</summary>
    public static SubprocessResiliencePolicy BridgeDefault => new()
    {
        Name = "bridge-subprocess",
        WriteTimeout = TimeSpan.FromSeconds(10),
        ReadTimeout = TimeSpan.FromSeconds(30),
        MaxRestarts = 3,
    };

    /// <summary>Doctor 子进程默认策略 — 10s 写超时、30s 读超时、最多 3 次重启</summary>
    public static SubprocessResiliencePolicy DoctorDefault => new()
    {
        Name = "doctor-subprocess",
        WriteTimeout = TimeSpan.FromSeconds(10),
        ReadTimeout = TimeSpan.FromSeconds(30),
        MaxRestarts = 3,
    };

    /// <summary>沙箱卫星进程默认策略 — 不重启，不健康时直接 Kill</summary>
    public static SubprocessResiliencePolicy SandboxDefault => new()
    {
        Name = "sandbox-satellite",
        WriteTimeout = TimeSpan.FromSeconds(10),
        ReadTimeout = TimeSpan.FromSeconds(30),
        MaxRestarts = 0,
        HealthCheck = new HealthCheckConfig { Action = UnhealthyAction.Kill },
    };
}
