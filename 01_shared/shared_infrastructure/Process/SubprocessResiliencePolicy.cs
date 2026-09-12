namespace Infrastructure.Subprocess;

public sealed class SubprocessResiliencePolicy
{
    public required string Name { get; init; }

    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public HealthCheckConfig HealthCheck { get; init; } = new();

    public int MaxRestarts { get; init; } = 3;

    public CircuitBreakerConfig CircuitBreaker { get; init; } = new()
    {
        FailureThreshold = 5,
        OpenDuration = TimeSpan.FromSeconds(60),
    };

    public static SubprocessResiliencePolicy Default(string name) => new() { Name = name };

    public static SubprocessResiliencePolicy BridgeDefault => new()
    {
        Name = "bridge-subprocess",
        WriteTimeout = TimeSpan.FromSeconds(10),
        ReadTimeout = TimeSpan.FromSeconds(30),
        MaxRestarts = 3,
    };

    public static SubprocessResiliencePolicy DoctorDefault => new()
    {
        Name = "doctor-subprocess",
        WriteTimeout = TimeSpan.FromSeconds(10),
        ReadTimeout = TimeSpan.FromSeconds(30),
        MaxRestarts = 3,
    };

    public static SubprocessResiliencePolicy SandboxDefault => new()
    {
        Name = "sandbox-satellite",
        WriteTimeout = TimeSpan.FromSeconds(10),
        ReadTimeout = TimeSpan.FromSeconds(30),
        MaxRestarts = 0,
        HealthCheck = new HealthCheckConfig { Action = UnhealthyAction.Kill },
    };
}
