namespace Infrastructure.Utils.Resilience;

/// <summary>
/// 熔断器开启异常 — 熔断器处于 Open 状态时调用受保护操作抛出
/// </summary>
public sealed class CircuitBreakerOpenException(string message) : Exception(message);

/// <summary>
/// 24h 重试预算耗尽异常 — TotalBudget 驱动模式下，重试总时长超过预算时抛出
/// </summary>
public sealed class NetworkRetryBudgetExhaustedException(string message) : Exception(message);

/// <summary>
/// 退避策略 — 重试间隔的计算方式
/// </summary>
public enum BackoffStrategy {
    /// <summary>固定间隔 — 每次重试等待 BaseDelay</summary>
    [EnumValue("fixed")]
    Fixed,
    /// <summary>线性退避 — 第 n 次重试等待 BaseDelay * n</summary>
    [EnumValue("linear")]
    Linear,
    /// <summary>指数退避 — 第 n 次重试等待 BaseDelay * 2^(n-1)</summary>
    [EnumValue("exponential")]
    Exponential,
    /// <summary>指数退避加抖动 — 在指数退避基础上叠加随机抖动因子（0.75~1.25），避免惊群</summary>
    [EnumValue("exponential_with_jitter")]
    ExponentialWithJitter
}

/// <summary>
/// 重试配置 — 控制最大重试次数、退避策略和重试预算
/// </summary>
public sealed class RetryConfig {
    /// <summary>最大重试次数，默认 3</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>基础退避延迟，默认 1 秒</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>单次退避最大延迟上限，默认 30 秒</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>退避策略，默认带抖动的指数退避</summary>
    public BackoffStrategy Strategy { get; init; } = BackoffStrategy.ExponentialWithJitter;

    /// <summary>异常判定函数 — 返回 true 时对该异常进行重试，null 时使用默认判定（超时/取消/HttpRequestException/IOException）</summary>
    public Func<Exception, bool>? ShouldRetry { get; init; }

    /// <summary>
    /// 重试总预算 — 设置后优先于 MaxRetries 驱动重试循环，预算耗尽抛 NetworkRetryBudgetExhaustedException
    /// <para>默认 null：回退到 MaxRetries 驱动（向后兼容）</para>
    /// </summary>
    public TimeSpan? TotalBudget { get; init; }

    /// <summary>
    /// 网络不可用时是否暂停预算计时 — true 时网络中断期间不消耗 TotalBudget，恢复后继续
    /// </summary>
    public bool PauseBudgetOnNetworkUnavailable { get; init; } = true;

    /// <summary>默认重试配置实例</summary>
    internal static readonly RetryConfig Default = new();
}

/// <summary>
/// 熔断器配置 — 控制失败阈值、开启时长和半开探测数
/// </summary>
public sealed class CircuitBreakerConfig {
    /// <summary>连续失败阈值，达到后熔断器开启，默认 5</summary>
    public int FailureThreshold { get; init; } = 5;

    /// <summary>熔断器开启持续时间，超时后进入半开状态，默认 30 秒</summary>
    public TimeSpan OpenDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>半开状态最大探测请求数，默认 1</summary>
    public int HalfOpenMaxProbe { get; init; } = 1;

    /// <summary>默认熔断器配置实例</summary>
    internal static readonly CircuitBreakerConfig Default = new();
}

/// <summary>
/// 健康检查配置 — 控制检查间隔、超时、失败阈值和不健康处置动作
/// </summary>
public sealed class HealthCheckConfig {
    /// <summary>健康检查间隔，默认 10 秒</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>单次健康检查超时，默认 5 秒</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>连续失败阈值，达到后触发不健康事件，默认 3</summary>
    public int FailureThreshold { get; init; } = 3;

    /// <summary>不健康处置动作，默认杀死并重启</summary>
    public UnhealthyAction Action { get; init; } = UnhealthyAction.KillAndRestart;

    /// <summary>默认健康检查配置实例</summary>
    internal static readonly HealthCheckConfig Default = new();
}

/// <summary>
/// 不健康处置动作 — 进程连续失败达阈值后采取的操作
/// </summary>
public enum UnhealthyAction {
    /// <summary>仅记录日志，不干预进程</summary>
    [EnumValue("log_only")]
    LogOnly,
    /// <summary>杀死进程</summary>
    [EnumValue("kill")]
    Kill,
    /// <summary>杀死进程并重启</summary>
    [EnumValue("kill_and_restart")]
    KillAndRestart
}

/// <summary>
/// 韧性策略 — 组合超时、重试、熔断和健康检查配置，描述单个通讯点的完整韧性策略
/// </summary>
public sealed class ResiliencePolicy {
    /// <summary>策略名称 — 用于日志和遥测标识</summary>
    public required string Name { get; init; }

    /// <summary>总超时 — 整个操作（含重试）的最大时长，null 表示不限制</summary>
    public TimeSpan? TotalTimeout { get; init; }

    /// <summary>单次操作超时 — 单次尝试的最大时长，null 表示不限制</summary>
    public TimeSpan? OperationTimeout { get; init; }

    /// <summary>重试配置，null 表示不重试</summary>
    public RetryConfig? Retry { get; init; }

    /// <summary>熔断器配置，null 表示不启用熔断</summary>
    public CircuitBreakerConfig? CircuitBreaker { get; init; }

    /// <summary>健康检查配置，null 表示不启用健康检查</summary>
    public HealthCheckConfig? HealthCheck { get; init; }

    /// <summary>
    /// HTTP 通讯默认策略 — 总超时 60s，单次 30s，3 次重试，5 次失败熔断 30s
    /// </summary>
    /// <param name="name">策略名称</param>
    /// <returns>HTTP 默认韧性策略</returns>
    public static ResiliencePolicy HttpDefault(string name) => new() {
        Name = name,
        TotalTimeout = TimeSpan.FromSeconds(60),
        OperationTimeout = TimeSpan.FromSeconds(30),
        Retry = new RetryConfig(),
        CircuitBreaker = new CircuitBreakerConfig(),
    };

    /// <summary>
    /// LLM 通讯默认策略 — 总超时 120s，单次 30s，3 次重试（1~30s 退避），5 次失败熔断 30s
    /// </summary>
    /// <param name="name">策略名称</param>
    /// <returns>LLM 默认韧性策略</returns>
    public static ResiliencePolicy LlmDefault(string name) => new() {
        Name = name,
        TotalTimeout = TimeSpan.FromSeconds(120),
        OperationTimeout = TimeSpan.FromSeconds(30),
        Retry = new RetryConfig { MaxRetries = 3, BaseDelay = TimeSpan.FromSeconds(1), MaxDelay = TimeSpan.FromSeconds(30) },
        CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 5, OpenDuration = TimeSpan.FromSeconds(30) },
    };

    /// <summary>
    /// 子进程默认策略 — 单次 30s，5 次失败熔断 60s，5s 间隔健康检查（3 次失败阈值）
    /// </summary>
    /// <param name="name">策略名称</param>
    /// <returns>子进程默认韧性策略</returns>
    public static ResiliencePolicy SubprocessDefault(string name) => new() {
        Name = name,
        OperationTimeout = TimeSpan.FromSeconds(30),
        CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 5, OpenDuration = TimeSpan.FromSeconds(60) },
        HealthCheck = new HealthCheckConfig { Interval = TimeSpan.FromSeconds(5), Timeout = TimeSpan.FromSeconds(5), FailureThreshold = 3 },
    };
}