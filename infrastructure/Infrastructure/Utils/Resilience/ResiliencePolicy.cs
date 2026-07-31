namespace Infrastructure.Utils.Resilience;

public sealed class CircuitBreakerOpenException(string message) : Exception(message);

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
