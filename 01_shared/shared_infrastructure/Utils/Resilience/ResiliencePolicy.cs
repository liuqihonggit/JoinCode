namespace Infrastructure.Utils.Resilience;

public sealed class CircuitBreakerOpenException(string message) : Exception(message);

/// <summary>
/// 24h 重试预算耗尽异常 — TotalBudget 驱动模式下，重试总时长超过预算时抛出
/// </summary>
public sealed class NetworkRetryBudgetExhaustedException(string message) : Exception(message);

public enum BackoffStrategy
{
    Fixed,
    Linear,
    Exponential,
    ExponentialWithJitter
}

public sealed class RetryConfig
{
    public int MaxRetries { get; init; } = 3;

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    public BackoffStrategy Strategy { get; init; } = BackoffStrategy.ExponentialWithJitter;

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

    internal static readonly RetryConfig Default = new();
}

public sealed class CircuitBreakerConfig
{
    public int FailureThreshold { get; init; } = 5;

    public TimeSpan OpenDuration { get; init; } = TimeSpan.FromSeconds(30);

    public int HalfOpenMaxProbe { get; init; } = 1;

    internal static readonly CircuitBreakerConfig Default = new();
}

public sealed class HealthCheckConfig
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public int FailureThreshold { get; init; } = 3;

    public UnhealthyAction Action { get; init; } = UnhealthyAction.KillAndRestart;

    internal static readonly HealthCheckConfig Default = new();
}

public enum UnhealthyAction
{
    LogOnly,
    Kill,
    KillAndRestart
}

public sealed class ResiliencePolicy
{
    public required string Name { get; init; }

    public TimeSpan? TotalTimeout { get; init; }

    public TimeSpan? OperationTimeout { get; init; }

    public RetryConfig? Retry { get; init; }

    public CircuitBreakerConfig? CircuitBreaker { get; init; }

    public HealthCheckConfig? HealthCheck { get; init; }

    public static ResiliencePolicy HttpDefault(string name) => new()
    {
        Name = name,
        TotalTimeout = TimeSpan.FromSeconds(60),
        OperationTimeout = TimeSpan.FromSeconds(30),
        Retry = new RetryConfig(),
        CircuitBreaker = new CircuitBreakerConfig(),
    };

    public static ResiliencePolicy LlmDefault(string name) => new()
    {
        Name = name,
        TotalTimeout = TimeSpan.FromSeconds(120),
        OperationTimeout = TimeSpan.FromSeconds(30),
        Retry = new RetryConfig { MaxRetries = 3, BaseDelay = TimeSpan.FromSeconds(1), MaxDelay = TimeSpan.FromSeconds(30) },
        CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 5, OpenDuration = TimeSpan.FromSeconds(30) },
    };

    public static ResiliencePolicy SubprocessDefault(string name) => new()
    {
        Name = name,
        OperationTimeout = TimeSpan.FromSeconds(30),
        CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 5, OpenDuration = TimeSpan.FromSeconds(60) },
        HealthCheck = new HealthCheckConfig { Interval = TimeSpan.FromSeconds(5), Timeout = TimeSpan.FromSeconds(5), FailureThreshold = 3 },
    };
}
